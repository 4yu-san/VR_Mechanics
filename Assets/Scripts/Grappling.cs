using UnityEngine;

public class Grappling: MonoBehaviour
{
    [Header("References")]
    private PlayerMovementGrappling pm;
    public Transform cam;
    public Transform gunTip;
    public LayerMask whatIsGrappleable;
    public LineRenderer lr;

    [Header("Grappling")]
    public float maxGrappleDistance;
    public float grappleDelayTime;

    private Vector3 grapplePoint;

    [Header("Cooldown")]
    public float grappleCooldown;
    public float grappleCooldownTimer;

    [Header("Input")]
    public KeyCode grappleKey = KeyCode.Mouse0;

    private bool grappling;

    private void Start()
    {
        pm = GetComponent<PlayerMovementGrappling>();
    }   

    private void StartGrapple()
    {
        if(grappleCooldownTimer>0) return;
        grappling = true;
        RaycastHit hit;
        if(Physics.Raycast(cam.position, cam.forward, out hit, maxGrappleDistance, whatIsGrappleable))
        {
            grapplePoint = hit.point;
            Invoke(nameof(ExecuteGrapple), grappleDelayTime);
        }
        else
        {
            grapplePoint = cam.position + cam.forward * maxGrappleDistance;
            Invoke(nameof(StopGrapple), grappleDelayTime);
        }

        lr.enabled = true;
        lr.SetPosition(0, gunTip.position);
    }
    private void ExecuteGrapple()
    {

    }
    private void StopGrapple()
    {
        grappling = false;
        grappleCooldownTimer = grappleCooldown;
        lr.enabled = false;

    }
    private void Update()
    {
        if (Input.GetKeyDown(grappleKey))
        {
            StartGrapple();
        }

        if(grappleCooldownTimer>0)
        {
            grappleCooldownTimer -= Time.deltaTime;
        }
    }
    private void LateUpdate()
    {
        if(grappling)
        {
            lr.SetPosition(0, gunTip.position);
            //lr.SetPosition(1, grapplePoint);
        }
    }
}
