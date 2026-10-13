// PlayModeChangeRecorder
// Attach to a GameObject (e.g. AttackRoot). Values are snapshotted when
// play mode starts, diffed when it ends, and ONLY the properties that
// actually changed are restored in edit mode and (optionally) applied as
// prefab overrides. Runtime-driven components (ParticleSystem, renderers
// for trails/lines, Animator, Rigidbody, AudioSource) are ignored so
// gameplay side effects never leak into prefabs. Editor-only logic lives
// in PlayModeSaver (Editor assembly); this component is just a marker.

using UnityEngine;

namespace Yeolha.Utility
{
    [DisallowMultipleComponent]
    public class PlayModeChangeRecorder : MonoBehaviour
    {
        [Tooltip("Restore play mode changes to this hierarchy after exiting play mode.")]
        public bool recordChanges = true;

        [Tooltip("After restoring, automatically apply the changes to the prefab this object belongs to.")]
        public bool applyToPrefab = true;

        [Tooltip("Also record ParticleSystem / ParticleSystemRenderer / TrailRenderer / LineRenderer property edits " +
                 "(constants, colors, enums; curves and gradients are not captured). Their active state is still ignored. " +
                 "Runtime-driven changes on them (e.g. charged start color) leak into the prefab if play mode exits mid-effect.")]
        public bool includeEffectComponents = false;
    }
}
