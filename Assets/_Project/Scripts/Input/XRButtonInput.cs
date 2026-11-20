using UnityEngine;
using UnityEngine.XR;

public class XRButtonInput : MonoBehaviour, IButtonInput
{
    public enum XRButton
    {
        Primary,     // A (Right) / X (Left)
        Secondary,   // B (Right) / Y (Left)
        Menu,        // Menu button (if available on device)
        Trigger,     // Index trigger (float threshold)
        Grip         // Grip (float threshold)
    }

    [Header("Which XR button to use")]
    public XRButton button = XRButton.Primary;

    [Header("Trigger/Grip threshold (only for Trigger/Grip)")]
    [Range(0.01f, 1f)] public float analogPressThreshold = 0.8f;

    [Header("Read from which hand")]
    public bool readLeftHand = true;
    public bool readRightHand = true;

    [Header("Fallback keyboard (optional)")]
    public bool allowKeyboard = true;
    public KeyCode keyboardKey = KeyCode.Space;

    // edge detection
    private bool _prevDown;

    public bool PressedThisFrame()
    {
        bool down = IsDownNow();

        bool pressedThisFrame = down && !_prevDown;
        _prevDown = down;

        if (pressedThisFrame)
        {
            Debug.Log("[XRButtonInput] PressedThisFrame detected.", this);
        }

        return pressedThisFrame;
    }

    public bool IsPressedNow()
    {
        return IsDownNow();
    }

    private bool IsDownNow()
    {
        // keyboard fallback
        if (allowKeyboard && Input.GetKey(keyboardKey))
            return true;

        bool down = false;

        if (readLeftHand)
            down |= ReadDevice(XRNode.LeftHand);

        if (readRightHand)
            down |= ReadDevice(XRNode.RightHand);

        return down;
    }

    private bool ReadDevice(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return false;

        switch (button)
        {
            case XRButton.Primary:
                return device.TryGetFeatureValue(CommonUsages.primaryButton, out bool pb) && pb;

            case XRButton.Secondary:
                return device.TryGetFeatureValue(CommonUsages.secondaryButton, out bool sb) && sb;

            case XRButton.Menu:
                return device.TryGetFeatureValue(CommonUsages.menuButton, out bool mb) && mb;

            case XRButton.Trigger:
                return device.TryGetFeatureValue(CommonUsages.trigger, out float t) && t >= analogPressThreshold;

            case XRButton.Grip:
                return device.TryGetFeatureValue(CommonUsages.grip, out float g) && g >= analogPressThreshold;
        }

        return false;
    }
}