namespace OlympUI; 

public enum CursorStyle {
    Normal,        // Arrow
    Pointer,       // Hand
    Text,          // I-beam
    Loading,       // Wait
    LoadingSmall,  // Small wait cursor (or Wait if not available)
    Crosshair,     // Crosshair
    ResizeN_S,     // Double arrow pointing north and south
    ResizeW_E,     // Double arrow pointing west and east
    ResizeNW_SE,   // Double arrow pointing northwest and southeast
    ResizeNE_SW,   // Double arrow pointing northeast and southwest
    ResizeN_S_W_E, // Four pointed arrow pointing north, south, east, and west
    Disabled,      // Slashed circle or crossbones
}