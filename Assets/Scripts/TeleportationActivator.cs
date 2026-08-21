using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TeleportationActivator : MonoBehaviour
{
    public XRRayInteractor teleportInteractor;
    public InputActionProperty teleportActivateAction;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        teleportInteractor.gameObject.SetActive(false);
        teleportActivateAction.action.performed += Action_performed;
    }

    private void Action_performed(InputAction.CallbackContext obj)
    {
        teleportInteractor.gameObject.SetActive(true);
    }

    // Update is called once per frame
    void Update()
    {
        if(teleportActivateAction.action.WasReleasedThisFrame())
        {
            teleportInteractor.gameObject.SetActive(false);
        }
    }
}
