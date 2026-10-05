# Bande musicale originale du film de Sol, synthétisée ici (rien de téléchargé, rien d'emprunté au film modèle).
# Structure calée sur le film : une nappe très discrète sous la question (0 – 19,5 s), une levée (19,5 – 21 s),
# puis un rythme à 120 temps par minute sous le produit (21 – 31 s) et une fin tenue (31 – 35 s).
#   python audio/bed.py  ->  audio/bed.wav (44,1 kHz, stéréo, 35 s)
import wave
import numpy as np

SR, DUR, BPM = 44100, 35.0, 120
BEAT = 60 / BPM
N = int(SR * DUR)
rng = np.random.default_rng(7)
L, R = np.zeros(N), np.zeros(N)


def put(sig, t, gain=1.0, pan=0.0):
    i = int(t * SR)
    if i >= N:
        return
    sig = sig[: N - i] * gain
    L[i:i + len(sig)] += sig * (1 - max(0, pan))
    R[i:i + len(sig)] += sig * (1 + min(0, pan))


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i, v in enumerate(x):          # filtre à un pôle : assez pour adoucir une nappe ou une basse
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def env(n, attack, release):
    e = np.ones(n)
    a, r = int(attack * SR), int(release * SR)
    e[:a] = np.linspace(0, 1, a)
    e[-r:] *= np.linspace(1, 0, r) ** 2
    return e


def tone(freq, seconds, shape="sine"):
    t = np.arange(int(seconds * SR)) / SR
    if shape == "sine":
        return np.sin(2 * np.pi * freq * t)
    # Piano électrique : fondamentale, un peu d'octave et de quinte, légère modulation
    return np.sin(2 * np.pi * freq * t + 0.6 * np.sin(2 * np.pi * freq * 2 * t) * np.exp(-t * 3)) + 0.25 * np.sin(2 * np.pi * freq * 2 * t) * np.exp(-t * 4)


def kick():
    t = np.arange(int(0.32 * SR)) / SR
    return np.sin(2 * np.pi * (48 * t + 60 * (1 - np.exp(-t * 28)) / 28)) * np.exp(-t * 11)


def clap():
    n = int(0.22 * SR)
    t = np.arange(n) / SR
    x = rng.standard_normal(n) * (np.exp(-t * 26) + 0.5 * np.exp(-np.abs(t - 0.012) * 300) + 0.4 * np.exp(-np.abs(t - 0.024) * 300))
    return x - lowpass(x, 900)


def hat(open_=False):
    n = int((0.16 if open_ else 0.05) * SR)
    t = np.arange(n) / SR
    x = rng.standard_normal(n) * np.exp(-t * (18 if open_ else 70))
    return x - lowpass(x, 6000)


NOTE = {"C": 261.63, "D": 293.66, "E": 329.63, "F": 349.23, "G": 392.0, "A": 440.0, "B": 493.88}
f = lambda name, octave=4: NOTE[name] * 2 ** (octave - 4)
# Quatre accords, deux temps... une mesure (2 s) chacun : Fmaj7, Em7, Dm7, Cmaj7
CHORDS = [
    (f("F", 2), [f("A", 3), f("C", 4), f("E", 4)]),
    (f("E", 2), [f("G", 3), f("B", 3), f("D", 4)]),
    (f("D", 2), [f("F", 3), f("A", 3), f("C", 4)]),
    (f("C", 2), [f("E", 3), f("G", 3), f("B", 3)]),
]

# 1. Nappe sous la question : un accord tenu, à peine audible, qui respire
for start in (0.0, 8.0, 14.0):
    for k, note in enumerate([f("F", 3), f("A", 3), f("C", 4), f("E", 4)]):
        n = int(7.5 * SR)
        x = lowpass(tone(note, 7.5) * env(n, 2.5, 3.0), 700)
        put(x, start, 0.035, pan=(-0.3, 0.3, -0.15, 0.15)[k])

# 2. L'attente (« if you had to think… ») : un battement sourd sur chaque temps, puis une levée
for i in range(6):
    put(kick() * 0.5, 16.5 + i * BEAT, 0.35)
n = int(1.5 * SR)
t = np.arange(n) / SR
riser = rng.standard_normal(n) * (t / 1.5) ** 2
put(riser - lowpass(riser, 1200 + 0), 19.5, 0.10)

# 3. Le rythme : mesures à partir de 21,0 s (les coupes du film tombent sur ses croches)
GROOVE, END = 21.0, 31.0
bars = int((END - GROOVE) / (4 * BEAT))
for bar in range(bars):
    t0 = GROOVE + bar * 4 * BEAT
    root, notes = CHORDS[bar % 4]
    for beat in range(4):
        put(kick(), t0 + beat * BEAT, 0.62)
        if beat in (1, 3):
            put(clap(), t0 + beat * BEAT, 0.20, pan=0.1)
        for half in range(2):
            swing = 0.03 if half else 0.0
            put(hat(open_=(beat == 3 and half == 1)), t0 + (beat + half * 0.5) * BEAT + swing, 0.07 if half else 0.045, pan=-0.25)
    # Basse : la fondamentale, rejouée en syncope
    for at, length in ((0, 0.9), (1.5, 0.4), (2.5, 0.9)):
        n = int(length * BEAT * SR)
        put(lowpass(tone(root, length * BEAT) * env(n, 0.005, 0.08), 320), t0 + at * BEAT, 0.42)
    # Accords au piano électrique, sur le temps et en contretemps
    for at, length, g in ((0, 1.4, 0.11), (2.5, 1.2, 0.085)):
        n = int(length * BEAT * SR)
        for k, note in enumerate(notes):
            put(lowpass(tone(note, length * BEAT, "ep") * env(n, 0.004, 0.5), 2400), t0 + at * BEAT, g, pan=(-0.35, 0.0, 0.35)[k])

# Pendant la levée (19,5 – 21,0) : charleston seul et basse, pour amener le rythme
for i in range(6):
    put(hat(), 19.5 + i * 0.25, 0.05, pan=-0.25)

# 4. Fin tenue : le dernier accord s'éteint sous l'adresse et l'appel à l'action
root, notes = CHORDS[0]
n = int(3.6 * SR)
for k, note in enumerate([root * 2] + notes):
    put(lowpass(tone(note, 3.6, "ep") * env(n, 0.01, 3.2), 1800), END, 0.10, pan=(0.0, -0.35, 0.0, 0.35)[k])

mix = np.stack([L, R], axis=1)
mix /= max(1.0, np.abs(mix).max() / 0.89)
with wave.open("audio/bed.wav", "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype("<i2").tobytes())
print("audio/bed.wav", round(len(mix) / SR, 2), "s, crête", round(float(np.abs(mix).max()), 2))
