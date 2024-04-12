using System;

namespace OlympUI.Events;

/// <summary>
/// Represents that an element should receive mouse events
/// </summary>
public interface IMouseEventReceiver : IEventReceive {
    public void OnMove(MouseEvent.Move e) { }
    public void OnEnter(MouseEvent.Enter e) { }
    public void OnLeave(MouseEvent.Leave e) { }
    public void OnDrag(MouseEvent.Drag e) { }
    public void OnPress(MouseEvent.Press e) { }
    public void OnRelease(MouseEvent.Release e) { }
    public void OnClick(MouseEvent.Click e) { }
    public void OnScroll(MouseEvent.Scroll e) { }
}