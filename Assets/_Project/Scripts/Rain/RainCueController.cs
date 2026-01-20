using UnityEngine;

public class RainCueController : MonoBehaviour
{
    public ParticleSystem rainFx;
    public RainSnapToHmd rainSnap;

    public void StartRain()
    {
        if (rainSnap != null)
            rainSnap.SnapToHmd();

        if (rainFx)
            rainFx.Play();
    }

    public void StopRain()
    {
        if (rainFx)
            rainFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
