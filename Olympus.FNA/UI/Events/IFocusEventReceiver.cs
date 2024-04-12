using System;

namespace OlympUI.Events;

/// <summary>
/// Represents that an element should receive focus events
/// </summary>
public interface IFocusEventReceiver : IEventReceive {
    /// <summary>
    /// The element has gained focus.
    /// </summary>
    /// <param name="e">The event.</param>
    public void OnFocus(FocusEvent.Focus e) { }
    
    /// <summary>
    /// The element has lost focus.
    /// </summary>
    /// <param name="e">The event.</param>
    public void OnUnFocus(FocusEvent.UnFocus e) { }
}
