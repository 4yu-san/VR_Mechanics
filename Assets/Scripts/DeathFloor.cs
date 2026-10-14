using UnityEngine;

// On the "DeathFloor" object (collider set to Is Trigger).
// When any part of the physics rig falls into it, it's game over.
public class DeathFloor : MonoBehaviour
{
    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<PhysicsRig>() == null) return;
        if (GameManager.instance.State != GameManager.GameState.Playing) return;

        GameManager.instance.GameOver();
    }
}
