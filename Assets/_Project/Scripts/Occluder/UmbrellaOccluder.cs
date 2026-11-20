using System.Collections;
using UnityEngine;

public class UmbrellaOccluder : MonoBehaviour, IOccluder
{
    [Header("Animation (on UmbrellaVisual)")]
    public Animator visualAnimator;          // 拖 UmbrellaVisual 的 Animator
    public AnimationClip openClip;           // 拖 Umbrella_Open.anim
    [Tooltip("Animator 里触发开伞的 Trigger 名称")]
    public string openTrigger = "OpenUmbrella";
    [Tooltip("Animator 里 Reset 状态/Clip 名称（第0帧默认）")]
    public string resetStateName = "Umbrella_Reset";

    [Header("40/70 transform (on THIS UmbrellaOccluder root)")]
    public Vector3 localPos40 = new Vector3(0f, -0.35f, 0.6f);
    public Vector3 localPos70 = new Vector3(0f, -0.35f, 0.6f);
    [Range(0.01f, 10f)] public float scale40 = 0.6f;
    [Range(0.01f, 10f)] public float scale70 = 1.0f;

    [Header("Anti-pop (recommended ON)")]
    public bool hideRenderersDuringInit = true;

    Coroutine hideCo;

    void Awake()
    {
        if (!visualAnimator)
            visualAnimator = GetComponentInChildren<Animator>(true);

        Hide();
    }

    public void Show(int ratio, float durationSec)
    {
        // 1) 先激活（否则找不到子组件），但先关渲染避免“闪一下/掉下来”
        gameObject.SetActive(true);

        Renderer[] renderers = null;
        if (hideRenderersDuringInit)
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) r.enabled = false;
        }

        // 2) 设置位置/大小（作用在 UmbrellaOccluder 根节点）
        transform.localPosition = (ratio == 70) ? localPos70 : localPos40;
        float s = (ratio == 70) ? scale70 : scale40;
        transform.localScale = Vector3.one * s;

        // 3) Animator 复位到 Reset 第0帧
        if (!visualAnimator)
        {
            Debug.LogError("[UmbrellaOccluder] visualAnimator is NULL. Please assign UmbrellaVisual's Animator.");
            // 重新开渲染，避免一直不可见
            if (renderers != null) foreach (var r in renderers) r.enabled = true;
            return;
        }

        visualAnimator.speed = 1f;
        visualAnimator.Play(resetStateName, 0, 0f);
        visualAnimator.Update(0f);

        // 4) 初始化完成后再开渲染
        if (renderers != null)
            foreach (var r in renderers) r.enabled = true;

        // 5) 动态调速：保证 durationSec 内播完 openClip
        if (openClip != null && durationSec > 0f)
            visualAnimator.speed = openClip.length / durationSec;
        else
            visualAnimator.speed = 1f;

        visualAnimator.ResetTrigger(openTrigger);
        visualAnimator.SetTrigger(openTrigger);

        // 6) 严格用 Manager 的 durationSec 隐藏（控制变量）
        if (hideCo != null) StopCoroutine(hideCo);
        hideCo = StartCoroutine(HideAfter(durationSec));
    }

    IEnumerator HideAfter(float durationSec)
    {
        yield return new WaitForSeconds(durationSec);

        if (visualAnimator) visualAnimator.speed = 1f;
        gameObject.SetActive(false);
    }

    public void Hide()
    {
        if (hideCo != null) StopCoroutine(hideCo);
        if (visualAnimator) visualAnimator.speed = 1f;
        gameObject.SetActive(false);
    }

}
