using System.Collections.Generic;
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

    private bool spatialMotionEnabled;
    private readonly List<Transform> staticAnimationRoots = new List<Transform>();
    private readonly List<Vector3> staticAnimationRootPositions = new List<Vector3>();
    private readonly List<Transform> staticRotationRoots = new List<Transform>();
    private readonly List<Quaternion> staticRootRotations = new List<Quaternion>();

    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        DisableLegacyAutoPlay();

        if (hideOnAwake)
            Hide();
    }

    public void Show(int ratio, float durationSec)
    {
        IsPlaying = true;

        PlaceOnceFromCurrentHmdPose();

        bool use70 = ratio == 70;
        GameObject activeObj = use70 ? visual70 : visual40;
        GameObject inactiveObj = use70 ? visual40 : visual70;

        if (inactiveObj) inactiveObj.SetActive(false);
        if (activeObj) activeObj.SetActive(true);

        if (playLegacyAnimations && activeObj)
        {
            if (!spatialMotionEnabled)
                CaptureStaticAnimationAnchors(activeObj);

            PlayLegacyAnimations(activeObj, durationSec);
        }
    }

    /// <summary>
    /// Static and Dynamic formal conditions share the same butterfly visuals.
    /// Only the Dynamic condition enables the legacy spatial group animation.
    /// The MaskingEventManager remains the sole owner of the event end time.
    /// </summary>
    public void SetSpatialMotionEnabled(bool enabled)
    {
        spatialMotionEnabled = enabled;
    }

    private void LateUpdate()
    {
        if (!IsPlaying || spatialMotionEnabled)
            return;

        // Preserve the authored per-butterfly layout while still allowing the
        // clip's descendant wing/body animation to run.
        int count = Mathf.Min(staticAnimationRoots.Count, staticAnimationRootPositions.Count);
        for (int i = 0; i < count; i++)
        {
            if (staticAnimationRoots[i] != null)
                staticAnimationRoots[i].localPosition = staticAnimationRootPositions[i];
        }

        int rotationCount = Mathf.Min(staticRotationRoots.Count, staticRootRotations.Count);
        for (int i = 0; i < rotationCount; i++)
        {
            if (staticRotationRoots[i] != null)
                staticRotationRoots[i].localRotation = staticRootRotations[i];
        }
    }

    private void CaptureStaticAnimationAnchors(GameObject root)
    {
        staticAnimationRoots.Clear();
        staticAnimationRootPositions.Clear();
        staticRotationRoots.Clear();
        staticRootRotations.Clear();

        Animation[] animations = root.GetComponentsInChildren<Animation>(true);
        foreach (Animation anim in animations)
        {
            if (anim == null)
                continue;

            CaptureStaticPosition(anim.transform);
            CaptureStaticRotation(anim.transform);

            Transform butterflyGroup = anim.transform.Find("butterfly_gruppe");
            CaptureStaticPosition(butterflyGroup);
        }
    }

    private void CaptureStaticPosition(Transform target)
    {
        if (target == null)
            return;

        staticAnimationRoots.Add(target);
        staticAnimationRootPositions.Add(target.localPosition);
    }

    private void CaptureStaticRotation(Transform target)
    {
        if (target == null)
            return;

        staticRotationRoots.Add(target);
        staticRootRotations.Add(target.localRotation);
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
        StopLegacyAnimations();

        if (visual40) visual40.SetActive(false);
        if (visual70) visual70.SetActive(false);

        staticAnimationRoots.Clear();
        staticAnimationRootPositions.Clear();
        staticRotationRoots.Clear();
        staticRootRotations.Clear();
        IsPlaying = false;
    }

    private void StopLegacyAnimations()
    {
        Animation[] animations = GetComponentsInChildren<Animation>(true);
        foreach (Animation anim in animations)
        {
            if (!anim) continue;
            anim.Rewind();
            anim.Sample();
            anim.Stop();
        }
    }
}
