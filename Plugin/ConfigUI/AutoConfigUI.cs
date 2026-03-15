// Plugin/ConfigUI/AutoConfigUI.cs
//
// PORTABLE MODULE — copy the entire ConfigUI/ folder into any Pulsar plugin project.
// Only requirement: update the namespace below to match your project.
//
using Sandbox.Graphics.GUI;
using System;
using System.Reflection;
using System.Text;
using VRage.Utils;    // MyGuiDrawAlignEnum
using VRageMath;

namespace PulsarTemplate.ConfigUI
{
    /*
     * AutoConfigUI
     * ------------
     * Reflects over a config object and generates GUI controls automatically.
     *
     * Supported field types:
     *   bool   -> MyGuiControlCheckbox
     *   int    -> MyGuiControlTextbox (numeric, validated on change)
     *   string -> MyGuiControlTextbox
     *   enum   -> MyGuiControlCombobox
     *
     * Usage:
     *   MyGuiSandbox.AddScreen(new AutoConfigUI(myConfigInstance));
     */
    public class AutoConfigUI : MyGuiScreenBase
    {
        private readonly object _config;

        // Layout constants
        private const float ITEM_HEIGHT  = 0.05f;
        private const float LABEL_WIDTH  = 0.25f;
        private const float CONTROL_WIDTH = 0.30f;
        private static readonly Vector2 SCREEN_SIZE = new Vector2(0.65f, 0.80f);

        public AutoConfigUI(object config)
            : base(
                position:          new Vector2(0.5f, 0.5f),
                backgroundColor:   new Vector4(0.08f, 0.08f, 0.10f, 0.95f),
                size:              SCREEN_SIZE,
                isTopMostScreen:   true,
                backgroundTexture: null)
        {
            _config          = config ?? throw new ArgumentNullException(nameof(config));
            EnabledBackgroundFade = true;
            CloseButtonEnabled    = true;
            RecreateControls(true);
        }

        public override string GetFriendlyName() => "AutoConfigUI";

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);

            // Title label
            Controls.Add(new MyGuiControlLabel(
                position: new Vector2(0f, -SCREEN_SIZE.Y / 2f + 0.03f),
                text:     "Plugin Configuration",
                originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP));

            float yOffset = -SCREEN_SIZE.Y / 2f + 0.10f;

            foreach (FieldInfo field in _config.GetType()
                         .GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                DrawField(field, ref yOffset);
                yOffset += ITEM_HEIGHT + 0.01f;
            }

            // Close button
            Controls.Add(new MyGuiControlButton(
                position:   new Vector2(0f, SCREEN_SIZE.Y / 2f - 0.06f),
                text:       new StringBuilder("Close"),
                onButtonClick: _ => CloseScreen()));
        }

        private void DrawField(FieldInfo field, ref float yOffset)
        {
            Type   type = field.FieldType;
            object val  = field.GetValue(_config);

            // Field name label on the left
            Controls.Add(new MyGuiControlLabel(
                position:    new Vector2(-SCREEN_SIZE.X / 2f + 0.03f, yOffset),
                text:        field.Name,
                originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));

            Vector2 ctrlPos = new Vector2(LABEL_WIDTH - SCREEN_SIZE.X / 2f + 0.03f, yOffset);

            if (type == typeof(bool))
            {
                var cb = new MyGuiControlCheckbox(position: ctrlPos, isChecked: (bool)val);
                cb.IsCheckedChanged += sender => field.SetValue(_config, sender.IsChecked);
                Controls.Add(cb);
            }
            else if (type == typeof(int) || type == typeof(string))
            {
                var tb = new MyGuiControlTextbox(
                    position:  ctrlPos,
                    defaultText: val?.ToString() ?? "",
                    maxLength:   64);
                tb.TextChanged += sender =>
                {
                    string text = sender.Text;
                    if (type == typeof(int))
                    {
                        if (int.TryParse(text, out int parsed))
                            field.SetValue(_config, parsed);
                    }
                    else
                    {
                        field.SetValue(_config, text);
                    }
                };
                Controls.Add(tb);
            }
            else if (type.IsEnum)
            {
                string[] names  = Enum.GetNames(type);
                Array    values = Enum.GetValues(type);

                var combo = new MyGuiControlCombobox(position: ctrlPos);
                for (int i = 0; i < names.Length; i++)
                    combo.AddItem((long)i, names[i]);

                int currentIndex = Array.IndexOf(values, val);
                if (currentIndex >= 0) combo.SelectItemByIndex(currentIndex);

                combo.ItemSelected += () =>
                {
                    object selected = values.GetValue((int)combo.GetSelectedKey());
                    field.SetValue(_config, selected);
                };
                Controls.Add(combo);
            }
        }
    }
}
