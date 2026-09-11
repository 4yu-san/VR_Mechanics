using UnityEngine;

public class PhysicsRig : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public Transform playerHead;
    public Transform leftController;
    public Transform rightController;

    public ConfigurableJoint leftHandJoint;
    public ConfigurableJoint rightHandJoint;
    public ConfigurableJoint headJoint;

    public CapsuleCollider playerCollider;

    public float bodyHeightMin = 0.5f;
    public float bodyHeightMax = 2.0f;

    // Update is called once per frame
    void FixedUpdate()
    {
        playerCollider.height = Mathf.Clamp(playerHead.localPosition.y, bodyHeightMin, bodyHeightMax);
        playerCollider.center = new Vector3(playerHead.localPosition.x, playerCollider.height / 2, playerHead.localPosition.z);

        leftHandJoint.targetPosition = leftController.localPosition;
        leftHandJoint.targetRotation = leftController.localRotation;

        rightHandJoint.targetPosition = rightController.localPosition;
        rightController.localRotation = rightController.localRotation;

        headJoint.targetPosition = playerHead.localPosition;
    }
}
