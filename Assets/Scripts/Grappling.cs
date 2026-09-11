using UnityEngine;
using UnityEngine.InputSystem;

public class Grappling: MonoBehaviour
{
    public Transform StartGrappleHand;
    public float maxDistance = 35f;
    public float pullingStrength;
    public LayerMask Grappleable;
    public InputActionProperty GrappleAction;
    public InputActionProperty PullAction;
    public Rigidbody playerrb;
    public LineRenderer lineRenderer;
    private SpringJoint joint;
    public Transform predictionPoint;
    private Vector3 grapplePoint;
    private bool hasHit;
    void Start(){

    }
    void Update(){
        GetGrapplePoint();

        if(GrappleAction.action.WasPressedThisFrame()){
            StartGrappling();
        }
        else if(GrappleAction.action.WasReleasedThisFrame()){
            StopGrappling();
        }
        PullRope();
        DrawRope();
    }
    
    public void PullRope(){
        if(!joint)
            return;
        
        if(PullAction.action.IsPressed())
        {
            Vector3 direction = (grapplePoint - StartGrappleHand.position).normalized;
            playerrb.AddForce(direction * pullingStrength * Time.deltaTime);

            float distance = Vector3.Distance(playerrb.position, grapplePoint);
            joint.maxDistance = distance;

        }
    }
    public void StartGrappling(){
        if(hasHit)
        {
            joint = playerrb.gameObject.AddComponent<SpringJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedAnchor = grapplePoint;

            float distance = Vector3.Distance(playerrb.position, grapplePoint);
            joint.maxDistance = distance;

            joint.spring = 4.5f;
            joint.damper = 7;
            joint.massScale = 4.5f;
        }
    }

    public void StopGrappling(){
        Destroy(joint);
    }

    public void GetGrapplePoint(){

        if(joint){
            predictionPoint.gameObject.SetActive(false);
            return;
        }

        RaycastHit raycastHit;
        hasHit = Physics.Raycast(StartGrappleHand.position, StartGrappleHand.forward, out raycastHit, maxDistance, Grappleable);

        if(hasHit){
            grapplePoint = raycastHit.point;
            predictionPoint.gameObject.SetActive(true);
            predictionPoint.position = grapplePoint;
        }
        else{
            predictionPoint.gameObject.SetActive(false);
        }

    }

    public void DrawRope(){
        if(!joint){
            lineRenderer.enabled = false;
        }

        else{
        lineRenderer.enabled = true;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, StartGrappleHand.position);
        lineRenderer.SetPosition(1, grapplePoint);
        }

    }
}
