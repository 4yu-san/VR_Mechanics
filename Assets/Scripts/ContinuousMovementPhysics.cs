using UnityEngine;
using UnityEngine.InputSystem;

public class ContinuousMovementPhysics : MonoBehaviour
{
    public float speed = 1.0f;
    public InputActionProperty moveAction;
    public Rigidbody rb;

    public Transform directionSource;

    private Vector2 inputAxis;

    // Update is called once per frame
    void Update()
    {
        inputAxis = moveAction.action.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        Quaternion yaw = Quaternion.Euler(0, directionSource.eulerAngles.y, 0);
        Vector3 direction = yaw * new Vector3(inputAxis.x, 0, inputAxis.y);

        rb.MovePosition(rb.position + direction * Time.fixedDeltaTime * speed);
    }
}
