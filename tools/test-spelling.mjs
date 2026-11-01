import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';
const source = readFileSync(new URL('../src/Browser.App/inject/spelling.js', import.meta.url), 'utf8');
function page() {
  class Element {
    constructor() { this.attrs = new Map(); this.style = {}; this.isConnected = true; this.tagName = 'DIV'; this.dataset = {}; }
    attachShadow() { return new Element(); }
    append() {} replaceChildren() {} remove() {}
    getAttribute(k) { return this.attrs.get(k) ?? null; }
    setAttribute(k,v) { this.attrs.set(k,v); }
    removeAttribute(k) { this.attrs.delete(k); }
    get spellcheck() { return this.getAttribute('spellcheck') !== 'false'; }
    set spellcheck(v) { this.setAttribute('spellcheck', String(v)); }
    closest() { return null; }
  }
  class Input extends Element { constructor() { super(); this.value = 'bonjor'; this.type = 'text'; } }
  class Textarea extends Input {}
  const document = { createElement: () => new Element(), createTextNode: () => ({}), documentElement: new Element(), hasFocus: () => true, addEventListener() {}, body: new Element() };
  const window = { document, HTMLInputElement: Input, HTMLTextAreaElement: Textarea, NodeFilter: {}, addEventListener() {} };
  const run = () => vm.runInNewContext(source, {window, performance});
  return {window, document, Input, run};
}
test('same-origin iframe uses its own element constructors and restores spellcheck', () => {
  const top = page(), child = page(), field = new child.Input();
  child.document.activeElement = field;
  top.document.activeElement = {tagName:'IFRAME', contentWindow:child.window};
  const api = top.run();
  assert.equal(api.read().text, 'bonjor');
  assert.equal(field.spellcheck, false);
  api.clear();
  assert.equal(field.getAttribute('spellcheck'), null);
});
test('sandboxed frame fails closed without throwing', () => {
  const top = page();
  top.document.activeElement = {tagName:'IFRAME', get contentWindow() { throw new Error('SecurityError'); }};
  assert.equal(top.run().read().frame, true);
});
test('idle field is not re-read until typing or repaint', () => {
  const top = page(), field = new top.Input();
  top.document.activeElement = field;
  top.window.getComputedStyle = () => ({});
  field.getBoundingClientRect = () => ({});
  const api = top.run();
  assert.equal(api.read().text, 'bonjor');
  api.paint('bonjor', [], '#000');
  assert.equal(api.read().idle, true);
});
test('username and card fields are never read', () => {
  const top = page(), field = new top.Input(); field.name = 'login_user';
  top.document.activeElement = field;
  assert.equal(top.run().read(), null);
  const card = page(), other = new card.Input(); other.autocomplete = 'cc-number';
  card.document.activeElement = other;
  assert.equal(card.run().read(), null);
});
test('open shadow input and oversize fallback restore original attribute', () => {
  const top = page(), field = new top.Input();
  field.setAttribute('spellcheck', 'true');
  top.document.activeElement = {shadowRoot:{activeElement:field}};
  const api = top.run();
  assert.equal(api.read().text, 'bonjor');
  field.value = 'a'.repeat(20001);
  assert.equal(api.read(), null);
  assert.equal(field.getAttribute('spellcheck'), 'true');
});
test('password fields remain native and are not read', () => {
  const top = page(), field = new top.Input(); field.type = 'password';
  top.document.activeElement = field;
  assert.equal(top.run().read(), null);
  assert.equal(field.getAttribute('spellcheck'), null);
});
