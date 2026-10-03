using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Browser.App;

/// <summary>Poignée entre les deux pages d'une vue scindée ; sous-classe uniquement pour poser le curseur de redimensionnement.</summary>
public sealed class DividerGrip : Grid
{
    public DividerGrip() => ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
}
