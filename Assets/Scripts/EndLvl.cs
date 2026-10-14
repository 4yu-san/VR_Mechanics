using UnityEngine;

// On the "Level End" object (collider set to Is Trigger).
// When any part of the physics rig enters it, the level is completed.
public class EndLvl : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        // Every rig collider (body, head, hands) lives under the PhysicsRig object
        if (other.GetComponentInParent<PhysicsRig>() == null) return;

        // Several rig colliders can enter in the same frame, only react once
        if (GameManager.instance.State != GameManager.GameState.Playing) return;

        GameManager.instance.LevelCompleted();
    }
}
