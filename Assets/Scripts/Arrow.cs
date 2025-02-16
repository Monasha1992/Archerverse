using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

public class Arrow : MonoBehaviour
{
    [SerializeField] private Rigidbody _rigidbody;
    [SerializeField] private Grabbable grabbable;

    [SerializeField] private HandGrabInteractable[] handGrabInteractables;

    private HandGrabInteractor _lastHandGrabInteractor;

    public HandGrabInteractor HandGrabber => _lastHandGrabInteractor;

    private UniqueIdentifier _identifier;

    private bool _hasPendingForce;
    private Vector3 _linearVelocity;

    private void Awake()
    {
        _identifier = UniqueIdentifier.Generate(Context.Global.GetInstance(), this);
    }

    private void OnEnable()
    {
        foreach (var handGrabInteractable in handGrabInteractables)
        {
            handGrabInteractable.WhenSelectingInteractorAdded.Action += HandleSelectingHandGrabInteractorAdded;
        }
    }

    private void OnDisable()
    {
        foreach (var handGrabInteractable in handGrabInteractables)
        {
            handGrabInteractable.WhenSelectingInteractorAdded.Action -= HandleSelectingHandGrabInteractorAdded;
        }
    }

    private void HandleSelectingHandGrabInteractorAdded(HandGrabInteractor interactor)
    {
        _lastHandGrabInteractor = interactor;
    }

    public void Attach()
    {
        var selfPose = transform.GetPose();
        // get the end of the arrow
        var arrowEndOffset = transform.forward * (transform.localScale.z / 2);
        selfPose.position -= arrowEndOffset - new Vector3(0, 0, 0.1f);
        grabbable.ProcessPointerEvent(new PointerEvent(_identifier.ID, PointerEventType.Hover, selfPose));
        grabbable.ProcessPointerEvent(new PointerEvent(_identifier.ID, PointerEventType.Select, selfPose));
        grabbable.ProcessPointerEvent(new PointerEvent(_identifier.ID, PointerEventType.Move, selfPose));
    }

    public void Move(Transform transform)
    {
        grabbable.ProcessPointerEvent(new PointerEvent(_identifier.ID, PointerEventType.Move, transform.GetPose()));
    }

    public void Eject(Vector3 force)
    {
        grabbable.ProcessPointerEvent(new PointerEvent(_identifier.ID, PointerEventType.Cancel,
            transform.GetPose()));

        _linearVelocity = force;
        _hasPendingForce = true;
    }

    private void FixedUpdate()
    {
        if (!_hasPendingForce) return;

        _hasPendingForce = false;
        _rigidbody.AddForce(_linearVelocity, ForceMode.VelocityChange);
        _rigidbody.AddTorque(Vector3.zero, ForceMode.VelocityChange);
    }
}