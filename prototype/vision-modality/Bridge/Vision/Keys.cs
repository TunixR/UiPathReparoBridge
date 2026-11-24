// PROTOTYPE for ReparoHome#49: throwaway, never merged. pyautogui key names -> Windows virtual keys.
using System;
using System.Collections.Generic;

namespace Reparo.UiPathBridge.Vision
{
    internal readonly struct Key
    {
        public readonly ushort Vk; public readonly bool Extended;
        public Key(ushort vk, bool extended = false) { Vk = vk; Extended = extended; }
        public override string ToString() => $"VK 0x{Vk:X2}{(Extended ? " ext" : "")}";
    }

    internal static class Keys
    {
        // Same spellings as the Playwright Bridge's DEFAULT_KEY_TABLE and the Reference client's KEY_ALIASES.
        static readonly Dictionary<string, Key> Table = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase)
        {
            ["ctrl"] = new Key(0xA2), ["control"] = new Key(0xA2), ["ctl"] = new Key(0xA2), ["ctrlleft"] = new Key(0xA2), ["lctrl"] = new Key(0xA2),
            ["ctrlright"] = new Key(0xA3, true), ["rctrl"] = new Key(0xA3, true),
            ["shift"] = new Key(0xA0), ["shiftleft"] = new Key(0xA0), ["lshift"] = new Key(0xA0), ["shiftright"] = new Key(0xA1), ["rshift"] = new Key(0xA1),
            ["alt"] = new Key(0xA4), ["option"] = new Key(0xA4), ["opt"] = new Key(0xA4), ["altleft"] = new Key(0xA4), ["lalt"] = new Key(0xA4),
            ["altright"] = new Key(0xA5, true), ["ralt"] = new Key(0xA5, true), ["altgr"] = new Key(0xA5, true),
            ["win"] = new Key(0x5B, true), ["winleft"] = new Key(0x5B, true), ["windows"] = new Key(0x5B, true), ["cmd"] = new Key(0x5B, true),
            ["command"] = new Key(0x5B, true), ["meta"] = new Key(0x5B, true), ["super"] = new Key(0x5B, true), ["winright"] = new Key(0x5C, true),
            ["enter"] = new Key(0x0D), ["return"] = new Key(0x0D), ["esc"] = new Key(0x1B), ["escape"] = new Key(0x1B), ["tab"] = new Key(0x09),
            ["space"] = new Key(0x20), ["spacebar"] = new Key(0x20), [" "] = new Key(0x20), ["backspace"] = new Key(0x08), ["bksp"] = new Key(0x08),
            ["delete"] = new Key(0x2E, true), ["del"] = new Key(0x2E, true), ["insert"] = new Key(0x2D, true), ["ins"] = new Key(0x2D, true),
            ["up"] = new Key(0x26, true), ["down"] = new Key(0x28, true), ["left"] = new Key(0x25, true), ["right"] = new Key(0x27, true),
            ["arrowup"] = new Key(0x26, true), ["arrowdown"] = new Key(0x28, true), ["arrowleft"] = new Key(0x25, true), ["arrowright"] = new Key(0x27, true),
            ["uparrow"] = new Key(0x26, true), ["downarrow"] = new Key(0x28, true), ["leftarrow"] = new Key(0x25, true), ["rightarrow"] = new Key(0x27, true),
            ["pageup"] = new Key(0x21, true), ["page_up"] = new Key(0x21, true), ["pgup"] = new Key(0x21, true),
            ["pagedown"] = new Key(0x22, true), ["page_down"] = new Key(0x22, true), ["pgdown"] = new Key(0x22, true),
            ["home"] = new Key(0x24, true), ["end"] = new Key(0x23, true),
            ["capslock"] = new Key(0x14), ["caps_lock"] = new Key(0x14), ["numlock"] = new Key(0x90, true), ["num_lock"] = new Key(0x90, true),
            ["scrolllock"] = new Key(0x91), ["scroll_lock"] = new Key(0x91), ["printscreen"] = new Key(0x2C, true), ["apps"] = new Key(0x5D, true),
        };
        static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["minus"] = "-", ["dash"] = "-", ["hyphen"] = "-", ["plus"] = "+", ["equal"] = "=", ["equals"] = "=", ["comma"] = ",",
            ["period"] = ".", ["dot"] = ".", ["slash"] = "/", ["backslash"] = "\\", ["semicolon"] = ";", ["quote"] = "'",
            ["apostrophe"] = "'", ["backtick"] = "`", ["grave"] = "`", ["tilde"] = "~",
        };

        static Keys() { for (int n = 1; n <= 24; n++) Table["f" + n] = new Key((ushort)(0x70 + n - 1)); }

        /// One key for hotkey/keydown/keyup. A single character is looked up in the target window's layout (its VK only:
        /// "ctrl a" is Ctrl + the A key). An unknown name throws, so a hotkey never half-presses (the Reference client's rule).
        public static Key Resolve(string name, IntPtr hkl)
        {
            if (Table.TryGetValue(name, out var k)) return k;
            if (Symbols.TryGetValue(name, out var s)) name = s;
            if (name.Length == 1)
            {
                short r = Native.VkKeyScanEx(name[0], hkl);
                if (r == -1) throw new ArgumentException($"key '{name}' isn't on the keyboard layout");
                return new Key((ushort)(r & 0xFF));
            }
            throw new ArgumentException($"unknown key '{name}'");
        }

        public static bool IsModifier(ushort vk) => vk is 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C or 0x10 or 0x11 or 0x12;

        /// As the Playwright Bridge's split_hotkey: "ctrl a", "ctrl+shift+s", "ctrl + shift + s", and a "+"/"-" that is itself the key.
        public static List<string> SplitHotkey(string hotkey)
        {
            var parts = new List<string>();
            var chunks = hotkey.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            for (int p = 0; p < chunks.Length; p++)
            {
                var chunk = chunks[p];
                if (chunk == "+" && p > 0 && p < chunks.Length - 1) continue;
                if (chunk.Length == 1) { parts.Add(chunk); continue; }
                var cur = "";
                for (int i = 0; i < chunk.Length; i++)
                {
                    char c = chunk[i];
                    bool sep = (c == '+' || c == '-') && cur != "" && i + 1 < chunk.Length && cur[^1] != '+' && cur[^1] != '-';
                    if (sep) { parts.Add(cur); cur = ""; } else cur += c;
                }
                if (cur != "") parts.Add(cur);
            }
            return parts;
        }
    }
}
