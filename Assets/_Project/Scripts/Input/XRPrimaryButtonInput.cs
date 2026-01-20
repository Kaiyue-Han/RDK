using UnityEngine;
using UnityEngine.XR;

public class XRPrimaryButtonInput : MonoBehaviour, IButtonInput
{
    [Header("Fallback keyboard (optional)")]
    public bool allowKeyboard = true;
    public KeyCode keyboardKey = KeyCode.Space;

    public bool PressedThisFrame()
    {
        // keyboard
        if (allowKeyboard && Input.GetKeyDown(keyboardKey))
            return true;

        // Controller
        bool pressed = false;

        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool lb) && lb)
            pressed = true;

        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (right.isValid && right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool rb) && rb)
            pressed = true;

        return pressed;
    }
}
