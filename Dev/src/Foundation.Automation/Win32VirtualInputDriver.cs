// @module: Foundation.Automation.Win32VirtualInputDriver
// @purpose: Focus-free window-scoped Win32 virtual pointer/keyboard input and UI control tree inspection without calling SendInput.
// @depends-on: System, System.Collections.Generic, System.Drawing, System.Linq, System.Reflection, System.Runtime.InteropServices, System.Windows.Forms
// @exports: Win32VirtualInputDriver

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Foundation.Automation;

/// <summary>
/// Dispatches pointer, keyboard, and UI-tree queries strictly to the target window/control
/// using window-scoped Win32 messages and WinForms control semantics without stealing host focus.
/// </summary>
public static class Win32VirtualInputDriver
{
    private const uint WmMouseMove = 0x0200, WmLButtonDown = 0x0201, WmLButtonUp = 0x0202, WmLButtonDblClk = 0x0203;
    private const uint WmRButtonDown = 0x0204, WmRButtonUp = 0x0205, WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmMouseWheel = 0x020A;
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmChar = 0x0102, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static AutomationControlNode? InspectTree(Control? root) =>
        root is null || root.IsDisposed ? null : InvokeOnUiThread(root, () => BuildNode(root, root, "0"));

    public static bool ExecutePointer(Control? root, AutomationPointerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (root is null || root.IsDisposed) return false;

        return InvokeOnUiThread(root, () =>
        {
            if (!root.IsHandleCreated) _ = root.Handle;
            Control target = ResolveTargetControl(root, request.TargetId, request.X, request.Y);
            if (!target.IsHandleCreated) _ = target.Handle;

            bool hasTargetId = !string.IsNullOrWhiteSpace(request.TargetId);
            Point pt = hasTargetId
                ? new Point(request.X > 0 ? request.X : Math.Max(1, target.Width / 2), request.Y > 0 ? request.Y : Math.Max(1, target.Height / 2))
                : TranslatePointToTarget(root, target, request.X, request.Y);
            IntPtr lParam = MakeLParam(pt.X, pt.Y);
            string action = (request.Action ?? "click").Trim().ToLowerInvariant();
            string button = (request.Button ?? "left").Trim().ToLowerInvariant();

            switch (action)
            {
                case "move": SendMessage(target.Handle, WmMouseMove, IntPtr.Zero, lParam); return true;
                case "click": DispatchMouseButton(target, button, lParam, isDoubleClick: false); return true;
                case "doubleclick": DispatchMouseButton(target, button, lParam, isDoubleClick: true); return true;
                case "drag":
                    Point endPt = hasTargetId
                        ? new Point(request.ToX ?? pt.X, request.ToY ?? pt.Y)
                        : TranslatePointToTarget(root, target, request.ToX ?? request.X, request.ToY ?? request.Y);
                    SendMessage(target.Handle, WmMouseMove, IntPtr.Zero, lParam);
                    SendMessage(target.Handle, WmLButtonDown, (IntPtr)1, lParam);
                    SendMessage(target.Handle, WmMouseMove, (IntPtr)1, MakeLParam(endPt.X, endPt.Y));
                    SendMessage(target.Handle, WmLButtonUp, IntPtr.Zero, MakeLParam(endPt.X, endPt.Y));
                    return true;
                case "scroll":
                    int delta = request.DeltaY == 0 ? 120 : request.DeltaY;
                    Point screenPt = target.PointToScreen(pt);
                    SendMessage(target.Handle, WmMouseWheel, (IntPtr)(delta << 16), MakeLParam(screenPt.X, screenPt.Y));
                    return true;
                default: throw new ArgumentException($"Unsupported pointer action '{request.Action}'.");
            }
        });
    }

    public static bool ExecuteKeyboard(Control? root, AutomationKeyboardRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (root is null || root.IsDisposed) return false;

        return InvokeOnUiThread(root, () =>
        {
            if (!root.IsHandleCreated) _ = root.Handle;
            Control target = !string.IsNullOrWhiteSpace(request.TargetId)
                ? (FindControlById(root, request.TargetId) ?? throw new ArgumentException($"Target control '{request.TargetId}' was not found."))
                : (root is ContainerControl cc && cc.ActiveControl is not null ? cc.ActiveControl : root);
            if (!target.IsHandleCreated) _ = target.Handle;

            string action = (request.Action ?? "type").Trim().ToLowerInvariant();
            switch (action)
            {
                case "type":
                    string text = request.Text ?? string.Empty;
                    if (target is TextBoxBase tb && !tb.ReadOnly) tb.AppendText(text);
                    else foreach (char ch in text) SendMessage(target.Handle, WmChar, (IntPtr)ch, IntPtr.Zero);
                    return true;
                case "press":
                case "hotkey":
                    int vk = ResolveVirtualKey(request.Key);
                    List<int> modVks = ResolveModifierKeys(request.Modifiers);
                    bool hasAlt = modVks.Contains(0x12);
                    foreach (int m in modVks) SendMessage(target.Handle, WmKeyDown, (IntPtr)m, IntPtr.Zero);
                    SendMessage(target.Handle, hasAlt ? WmSysKeyDown : WmKeyDown, (IntPtr)vk, IntPtr.Zero);
                    SendMessage(target.Handle, hasAlt ? WmSysKeyUp : WmKeyUp, (IntPtr)vk, IntPtr.Zero);
                    for (int i = modVks.Count - 1; i >= 0; i--) SendMessage(target.Handle, WmKeyUp, (IntPtr)modVks[i], IntPtr.Zero);
                    if (action == "hotkey" && modVks.Contains(0x11) && vk == 'A' && target is TextBoxBase tbSelect)
                        tbSelect.SelectAll();
                    return true;
                default: throw new ArgumentException($"Unsupported keyboard action '{request.Action}'.");
            }
        });
    }

    internal static Control? FindControlById(Control root, string? targetId) =>
        string.IsNullOrWhiteSpace(targetId) ? null : FindControlRecursive(root, targetId.Trim(), "0");

    private static Control? FindControlRecursive(Control node, string targetId, string indexPath)
    {
        if (string.Equals(ComputeControlId(node, indexPath), targetId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(node.Name, targetId, StringComparison.OrdinalIgnoreCase))
            return node;

        for (int i = 0; i < node.Controls.Count; i++)
            if (FindControlRecursive(node.Controls[i], targetId, $"{indexPath}.{i}") is { } match) return match;
        return null;
    }

    private static AutomationControlNode BuildNode(Control root, Control current, string indexPath)
    {
        Point loc = GetRelativeLocation(root, current);
        var children = new List<AutomationControlNode>(current.Controls.Count);
        for (int i = 0; i < current.Controls.Count; i++)
            children.Add(BuildNode(root, current.Controls[i], $"{indexPath}.{i}"));

        return new AutomationControlNode(
            ComputeControlId(current, indexPath), current.GetType().Name, current.Name ?? string.Empty,
            current.Text ?? string.Empty, loc.X, loc.Y, current.Width, current.Height,
            current.Visible, current.Enabled, children);
    }

    private static string ComputeControlId(Control c, string indexPath) =>
        !string.IsNullOrWhiteSpace(c.Name) ? c.Name : $"{c.GetType().Name}@{indexPath}";

    private static Point GetRelativeLocation(Control root, Control current)
    {
        if (ReferenceEquals(root, current)) return new Point(0, 0);
        int x = 0, y = 0;
        for (Control? cur = current; cur is not null && !ReferenceEquals(cur, root); cur = cur.Parent)
        {
            x += cur.Left;
            y += cur.Top;
        }
        return new Point(x, y);
    }

    private static Control ResolveTargetControl(Control root, string? targetId, int x, int y)
    {
        if (!string.IsNullOrWhiteSpace(targetId))
            return FindControlById(root, targetId) ?? throw new ArgumentException($"Target control '{targetId}' was not found.");

        Control cur = root;
        Point rel = new(x, y);
        while (true)
        {
            Control? child = cur.GetChildAtPoint(rel, GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled);
            if (child is null || ReferenceEquals(child, cur)) break;
            rel = new Point(rel.X - child.Left, rel.Y - child.Top);
            cur = child;
        }
        return cur;
    }

    private static Point TranslatePointToTarget(Control root, Control target, int rootX, int rootY)
    {
        if (ReferenceEquals(root, target)) return new Point(rootX, rootY);
        Point origin = GetRelativeLocation(root, target);
        return new Point(rootX - origin.X, rootY - origin.Y);
    }

    private static void DispatchMouseButton(Control target, string button, IntPtr lParam, bool isDoubleClick)
    {
        SendMessage(target.Handle, WmMouseMove, IntPtr.Zero, lParam);
        if (button == "right") { SendMessage(target.Handle, WmRButtonDown, (IntPtr)2, lParam); SendMessage(target.Handle, WmRButtonUp, IntPtr.Zero, lParam); return; }
        if (button == "middle") { SendMessage(target.Handle, WmMButtonDown, (IntPtr)16, lParam); SendMessage(target.Handle, WmMButtonUp, IntPtr.Zero, lParam); return; }

        SendMessage(target.Handle, isDoubleClick ? WmLButtonDblClk : WmLButtonDown, (IntPtr)1, lParam);
        SendMessage(target.Handle, WmLButtonUp, IntPtr.Zero, lParam);

        if (target.Enabled)
        {
            if (target is Button btn)
            {
                if (btn.CanSelect) btn.PerformClick();
                else typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(btn, new object[] { EventArgs.Empty });
            }
            else if (target is CheckBox chk) chk.Checked = !chk.Checked;
            else if (target is RadioButton rb) rb.Checked = true;
        }
    }

    private static IntPtr MakeLParam(int x, int y) => (IntPtr)((y << 16) | (x & 0xFFFF));

    private static List<int> ResolveModifierKeys(IReadOnlyList<string>? modifiers) =>
        modifiers is null ? new List<int>() : modifiers.Select(m => m.Trim().ToLowerInvariant() switch
        {
            "ctrl" or "control" => 0x11, "shift" => 0x10, "alt" or "menu" => 0x12, _ => 0
        }).Where(vk => vk != 0).ToList();

    private static int ResolveVirtualKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Key name is required.");
        string k = key.Trim().ToUpperInvariant();
        return k switch
        {
            "ENTER" or "RETURN" => 0x0D, "ESC" or "ESCAPE" => 0x1B, "TAB" => 0x09, "SPACE" => 0x20,
            "BACK" or "BACKSPACE" => 0x08, "DELETE" or "DEL" => 0x2E, "LEFT" => 0x25, "UP" => 0x26,
            "RIGHT" => 0x27, "DOWN" => 0x28, "HOME" => 0x24, "END" => 0x23,
            _ when k.Length == 1 && char.IsAsciiLetterOrDigit(k[0]) => k[0],
            _ when k.StartsWith("F", StringComparison.Ordinal) && int.TryParse(k[1..], out int fn) && fn is >= 1 and <= 12 => 0x6F + fn,
            _ => throw new ArgumentException($"Unsupported key '{key}'.")
        };
    }

    internal static T InvokeOnUiThread<T>(Control root, Func<T> action) =>
        root.IsHandleCreated && root.InvokeRequired ? (T)root.Invoke(action) : action();
}
