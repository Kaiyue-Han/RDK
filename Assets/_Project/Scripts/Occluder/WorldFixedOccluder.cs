using System.Collections;
using UnityEngine;

public class WorldFixedOccluder : MonoBehaviour, IOccluder
{
    [Header("References")]
    [Tooltip("Use the HMD / Main Camera transform. Position is sampled once when Show() is called.")]
    [SerializeField] private Transform hmd;

    [Header("Assign visuals")]
    [SerializeField] private GameObject visual40;
    [SerializeField] private GameObject visual70;

    [Header("Spawn Pose")]
    [Tooltip("Distance in front of the HMD at trigger time.")]
    [SerializeField] private float spawnDistance = 1.4f;

    [Tooltip("Added to HMD height. Use 0 for eye height; negative values place it slightly lower.")]
    [SerializeField] private float heightOffset = 0f;

    [Tooltip("If enabled, the root faces the user at spawn time. It will not keep looking at the user afterwards.")]
    [SerializeField] private bool faceUserOnSpawn = true;

    [Tooltip("Extra rotation applied after facing the user. Use Y=180 if the prefab appears backwards.")]
    [SerializeField] private Vector3 spawnEulerOffset = Vector3.zero;

    [Header("Legacy Animation Optional")]
    [Tooltip("Enable this for butterfly groups using the old Animation component.")]
    [SerializeField] private bool playLegacyAnimations = false;

    [SerializeField] private string legacyClipName = "Take 001";

    [Tooltip("If true, animation speed is adjusted so the clip fits the occlusion duration.")]
    [SerializeField] private bool fitLegacyAnimationToDuration = true;

    [Tooltip("Used only when Fit Legacy Animation To Duration is false.")]
    [SerializeField] private float legacyAnimationSpeed = 1f;

    [Header("Runtime")]
    [SerializeField] private bool hideOnAwake = true;

    private Coroutine playCo;

    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        DisableLegacyAutoPlay();

        if (hideOnAwake)
            Hide();
    }

    public void Show(int ratio, float durationSec)
    {
        if (playCo != null)
        {
            StopCoroutine(playCo);
            playCo = null;
        }

        playCo = StartCoroutine(PlayOcclusion(ratio, durationSec));
    }

    private IEnumerator PlayOcclusion(int ratio, float durationSec)
    {
        IsPlaying = true;

        PlaceOnceFromCurrentHmdPose();

        bool use70 = ratio == 70;
        GameObject activeObj = use70 ? visual70 : visual40;
        GameObject inactiveObj = use70 ? visual40 : visual70;

        if (inactiveObj) inactiveObj.SetActive(false);
        if (activeObj) activeObj.SetActive(true);

        if (playLegacyAnimations && activeObj)
            PlayLegacyAnimations(activeObj, durationSec);

        if (durationSec > 0f)
            yield return new WaitForSeconds(durationSec);

        Hide();
    }

    private void PlaceOnceFromCurrentHmdPose()
    {
        if (!hmd)
        {
            Debug.LogWarning("[WorldFixedOccluder] HMD transform is not assigned. Cannot place relative to user.", this);
            return;
        }

        Vector3 flatForward = hmd.forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = Vector3.forward;

        flatForward.Normalize();

        Vector3 spawnPos = hmd.position + flatForward * spawnDistance;
        spawnPos.y = hmd.position.y + heightOffset;
        transform.position = spawnPos;

        if (faceUserOnSpawn)
        {
            // For a normal Unity model whose front is +Z, -flatForward makes it face back toward the user.
            Quaternion faceUserRot = Quaternion.LookRotation(-flatForward, Vector3.up);
            transform.rotation = faceUserRot * Quaternion.Euler(spawnEulerOffset);
        }
    }

    private void DisableLegacyAutoPlay()
    {
        Animation[] animations = GetComponentsInChildren<Animation>(true);
        foreach (Animation anim in animations)
        {
            if (!anim) continue;
            anim.playAutomatically = false;
        }
    }

    private void PlayLegacyAnimations(GameObject root, float durationSec)
    {
        Animation[] animations = root.GetComponentsInChildren<Animation>(true);

        foreach (Animation anim in animations)
        {
            if (!anim) continue;

            AnimationState state = FindAnimationState(anim);
            if (state == null)
            {
                Debug.LogWarning($"[WorldFixedOccluder] No usable AnimationState found on {anim.name}.", anim);
                continue;
            }

            anim.Stop();
            state.time = 0f;
            state.wrapMode = WrapMode.Once;

            if (fitLegacyAnimationToDuration && durationSec > 0.01f)
                state.speed = Mathf.Max(0.01f, state.length / durationSec);
            else
                state.speed = legacyAnimationSpeed;

            anim.Play(state.name);
        }
    }

    private AnimationState FindAnimationState(Animation anim)
    {
        if (!string.IsNullOrEmpty(legacyClipName))
        {
            AnimationState namedState = anim[legacyClipName];
            if (namedState != null)
                return namedState;
        }

        if (anim.clip != null)
        {
            AnimationState clipState = anim[anim.clip.name];
            if (clipState != null)
                return clipState;
        }

        foreach (AnimationState state in anim)
            return state;

        return null;
    }

    public void Hide()
    {
        if (playCo != null)
        {
            StopCoroutine(playCo);
            playCo = null;
        }

        StopLegacyAnimations();

        if (visual40) visual40.SetActive(false);
        if (visual70) visual70.SetActive(false);

        IsPlaying = false;
    }

    private void StopLegacyAnimations()
    {
        Animation[] animations = GetComponentsInChildren<Animation>(true);
        foreach (Animation anim in animations)
        {
            if (!anim) continue;
            anim.Stop();
        }
    }
}
