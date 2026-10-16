using UnityEngine;

namespace BlenderSceneControls
{
    // Capture the event before Use/phase changes. Phases do not own native GUI events.
    internal readonly struct ModalInput
    {
        internal readonly EventType Type;
        internal readonly KeyCode Key;
        internal readonly char Character;
        internal readonly int Button;
        internal readonly bool Shift;
        internal readonly bool Control;
        internal readonly bool Command;
        internal readonly bool Alt;
        internal readonly bool IsKey;
        internal readonly bool IsMouse;
        internal readonly Vector2 MousePosition;
        internal readonly Vector2 Scroll;

        internal ModalInput(EventType type, KeyCode key = KeyCode.None, char character = '\0', int button = 0,
            bool shift = false, bool control = false, bool command = false, bool alt = false)
        {
            Type = type;
            Key = key;
            Character = character;
            Button = button;
            Shift = shift;
            Control = control;
            Command = command;
            Alt = alt;
            IsKey = type == EventType.KeyDown || type == EventType.KeyUp;
            IsMouse = type == EventType.MouseDown || type == EventType.MouseUp ||
                type == EventType.MouseMove || type == EventType.MouseDrag || type == EventType.ContextClick ||
                type == EventType.MouseEnterWindow || type == EventType.MouseLeaveWindow;
            MousePosition = Vector2.zero;
            Scroll = Vector2.zero;
        }

        internal ModalInput(Event current) : this(current.type, current.keyCode, current.character, current.button,
            current.shift, current.control, current.command, current.alt)
        {
            IsKey = current.isKey;
            IsMouse = current.isMouse;
            MousePosition = current.mousePosition;
            Scroll = current.delta;
        }
    }
}
