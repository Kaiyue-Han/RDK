using UnityEngine;

public class UIOccluder : MonoBehaviour, IOccluder
{
    [Header("Canvas (root)")]
    public Canvas canvas;

    [Header("Assign two panels (GameObjects)")]
    public GameObject panel40;
    public GameObject panel70;

    void Awake()
    {
        Hide();
    }

    public void Show(int ratio)
    {
        if (canvas) canvas.enabled = true;

        if (panel40) panel40.SetActive(ratio == 40);
        if (panel70) panel70.SetActive(ratio == 70);
    }

    public void Hide()
    {
        if (panel40) panel40.SetActive(false);
        if (panel70) panel70.SetActive(false);

        if (canvas) canvas.enabled = false;
    }
}