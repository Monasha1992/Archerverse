using System;
using System.Collections;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

public class Bow : MonoBehaviour, ITransformer
{
    [SerializeField] private Pose naturalPose;
    [SerializeField] private Transform holder;
    [SerializeField] private Transform leftRubberPoint;
    [SerializeField] private Transform rightRubberPoint;
    [SerializeField] private float rubberAngle = 60f;

    [SerializeField] private AnimationCurve translationResistance;
    [SerializeField] private AnimationCurve aimingResistance;
    [SerializeField] private float springForce = 0.1f;
    [SerializeField] private float damping = 0.95f;
    [SerializeField] private float arrowStrength = 10f;

    [SerializeField] private HandGrabInteractable[] handGrabInteractables;

    [Header("Feedback")] [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip stretchAudioClip;
    [SerializeField] private AnimationCurve stretchAudioPitch;
    [SerializeField] private AnimationCurve stretchAudioStep;

    private IGrabbable _grabbable;
    private Pose _grabDeltaInLocalSpace;

    private bool _isGrabbed;

    private Vector3 _positionVelocity = Vector3.zero;
    private float _rotationVelocity;

    private Arrow _loadedArrow;

    private readonly WaitForSeconds _hapticsWait = new(TensionStepLength * 0.5f);

    private float _lastTensionStep;
    private float _lastTensionTime;
    private const float TensionStepLength = 0.1f;

    public void Initialize(IGrabbable grabbable)
    {
        _grabbable = grabbable;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_loadedArrow != null) return;

        if (other.TryGetComponent(out Arrow arrow)) HandleArrowSnapped(arrow);
    }

    private void HandleArrowSnapped(Arrow arrow)
    {
        var handGrabInteractor = arrow.HandGrabber;
        if (handGrabInteractor == null
            || handGrabInteractor.State != InteractorState.Select)
        {
            return;
        }

        foreach (var interactable in handGrabInteractables)
        {
            if (!handGrabInteractor.CanInteractWith(interactable)) continue;

            handGrabInteractor.ForceRelease();
            handGrabInteractor.ForceSelect(interactable, true);
            _loadedArrow = arrow;
            _loadedArrow.Attach();
            return;
        }
    }

    public void BeginTransform()
    {
        var grabPoint = _grabbable.GrabPoints[0];
        var targetTransform = _grabbable.Transform;
        _grabDeltaInLocalSpace = new Pose(
            targetTransform.InverseTransformVector(grabPoint.position - targetTransform.position),
            Quaternion.Inverse(grabPoint.rotation) * targetTransform.rotation);

        _isGrabbed = true;
        _positionVelocity = Vector3.zero;
        _rotationVelocity = 0f;

        CurveHolder(rubberAngle);
    }

    public void UpdateTransform()
    {
        var grabPoint = _grabbable.GrabPoints[0];
        var targetTransform = _grabbable.Transform;

        var currentPosition = targetTransform.localPosition;
        var desiredPosition = grabPoint.position - targetTransform.TransformVector(_grabDeltaInLocalSpace.position);
        var desiredRotation = grabPoint.rotation * _grabDeltaInLocalSpace.rotation;
        var desiredLocalPose = PoseUtils.Delta(targetTransform.parent, new Pose(desiredPosition, desiredRotation));

        var aimVector = (desiredLocalPose.position - naturalPose.position);

        var tension = Vector3.Distance(currentPosition, naturalPose.position);
        var desiredTension = aimVector.magnitude;

        if (desiredTension > tension)
        {
            var tr = translationResistance.Evaluate(tension) * Time.deltaTime;
            desiredTension = Mathf.MoveTowards(tension, desiredTension, tr);
        }

        var idealAim = Vector3.ProjectOnPlane(aimVector, Vector3.right).normalized;
        aimVector = Vector3.Slerp(aimVector, idealAim, aimingResistance.Evaluate(tension)).normalized;

        var targetPosition = naturalPose.position + aimVector * desiredTension;

        targetTransform.localPosition = targetPosition;

        tension = Tension(targetPosition);
        var rotationResistance = aimingResistance.Evaluate(tension);
        var aimingDirection = Quaternion.LookRotation(-aimVector, desiredLocalPose.up);

        targetTransform.localRotation =
            Quaternion.SlerpUnclamped(desiredLocalPose.rotation, aimingDirection, rotationResistance);

        OnStretch(tension);
    }

    public void EndTransform()
    {
        _isGrabbed = false;
        if (_loadedArrow != null)
        {
            var force = ArrowLaunchForce();
            _loadedArrow.Eject(force);
            _loadedArrow = null;
        }

        CurveHolder(0f);
    }

    private void Update()
    {
        if (_isGrabbed) return;

        var targetTransform = transform;

        var force = (naturalPose.position - targetTransform.localPosition) * springForce;
        _positionVelocity = _positionVelocity * damping + force * Time.deltaTime;
        targetTransform.localPosition += _positionVelocity;

        targetTransform.localRotation.ToAngleAxis(out var angle, out var axis);
        if (angle > 180) angle -= 360;

        _rotationVelocity = _rotationVelocity * damping + angle * springForce * Time.deltaTime;
        targetTransform.localRotation = Quaternion.AngleAxis(_rotationVelocity, -axis.normalized) *
                                        targetTransform.localRotation;
    }

    private void LateUpdate()
    {
        if (_loadedArrow != null)
        {
            _loadedArrow.Move(holder);
        }
    }


    private void CurveHolder(float angle)
    {
        rightRubberPoint.localEulerAngles = Vector3.up * angle;
        leftRubberPoint.localEulerAngles = -Vector3.up * angle;
    }


    private float Tension(Vector3 localPoint)
    {
        return Vector3.Distance(localPoint, naturalPose.position);
    }

    private Vector3 ArrowLaunchForce()
    {
        var targetTransform = _grabbable.Transform;
        var tension = Tension(targetTransform.localPosition);
        var direction = (targetTransform.parent.position - targetTransform.position).normalized;

        return direction * tension * arrowStrength;
    }


    public void OnStretch(float currentTension)
    {
        if (Mathf.Abs(_lastTensionStep - currentTension) > stretchAudioStep.Evaluate(currentTension)
            && (Time.unscaledTime - _lastTensionTime) > TensionStepLength)
        {
            PlayStretchAudio(currentTension);
            PlayStretchHaptics(currentTension);
            _lastTensionStep = currentTension;
            _lastTensionTime = Time.unscaledTime;
        }
    }

    private void PlayStretchAudio(float tension)
    {
        var pitch = stretchAudioPitch.Evaluate(tension);
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(stretchAudioClip, 1f);
    }

    private void PlayStretchHaptics(float tension)
    {
        var pitch = stretchAudioPitch.Evaluate(tension);
        StartCoroutine(HapticsRoutine(pitch));
    }

    private IEnumerator HapticsRoutine(float pitch)
    {
        var controllers = OVRInput.Controller.RTouch | OVRInput.Controller.LTouch;
        OVRInput.SetControllerVibration(pitch * 0.5f, pitch * 0.2f, controllers);
        yield return _hapticsWait;
        OVRInput.SetControllerVibration(0, 0, controllers);
    }
}