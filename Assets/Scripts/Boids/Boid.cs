using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class Boid : MonoBehaviour
{
    [SerializeField]
    private float cohesionWeight = 0.16f; // Weight for cohesion behavior
    [SerializeField]
    private float alignmentWeight = 0.36f; // Weight for alignment behavior
    [SerializeField]
    private float separationWeight = 0.5f; // Weight for separation behavior
    [SerializeField]
    private float avoidanceWeight = 1f; // Weight for alignment behavior
    [SerializeField]
    private float maxSpeed = 2f; // Maximum speed of the boids
    [SerializeField]
    private float maxForce = 1f;

    [Space]

    [SerializeField, Range(0f, 20f)]
    private float cohesionRadius = 8f;
    [SerializeField, Range(0f, 20f)]
    private float alignmentRadius = 5.5f;
    [SerializeField, Range(0f, 20f)]
    private float seperationRadius = 2.5f;

    //[SerializeField, Range(0f, 360f)]
    private float cohesionAngle = 360f;
    //[SerializeField, Range(0f, 360f)]
    private float alignmentAngle = 360f;

    [SerializeField]
    private LayerMask obstacleMask = new LayerMask().ToEverything();
    [SerializeField]
    private float viewSphereCastRadius = 0.1f;

    [Space]

    [SerializeField]
    private bool randomiseStartPosition;

    [SerializeField, ReadOnly]
    private Vector3 cohesion;
    [SerializeField, ReadOnly]
    private Vector3 alignment;
    [SerializeField, ReadOnly]
    private Vector3 separation;

    private List<Boid> neighborsToCohese = new();
    private List<Boid> neighborsToAllign = new();
    private List<Boid> neighborsToSeperate = new();
    private Vector3 centerOfMass;
    private Vector3 lastFramePosition;
    private float lastDeltaTime = float.PositiveInfinity;
    [SerializeField, ReadOnly]
    private Vector3 velocity;

    [SerializeField]
    private TriggerEventListener cohesionTrigger;
    [SerializeField]
    private TriggerEventListener alignmentTrigger;
    [SerializeField]
    private TriggerEventListener seperationTrigger;
    private NavMeshAgent agent;

    private const float debugLineThickness = 4f;

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        DebugDrawRadius(cohesionRadius, Color.cyan, cohesionAngle);
        DebugDrawRadius(alignmentRadius, Color.yellow, alignmentAngle);
        DebugDrawRadius(seperationRadius, Color.red);
        //DebugDrawConnections();
        //DebugDrawDirections();
        //LookAround(DebugDrawLookPoints);
        DebugDrawCenterOfMass();
    }

    private void DebugDrawRadius(float radius, Color outlineColor, float angle = 360f)
    {
        if (radius > 0f)
        {
            Handles.color = outlineColor;

            Vector3 startVector = transform.forward;
            if (angle < 360f)
            {
                startVector = Quaternion.Euler(0f, -angle * 0.5f, 0f) * startVector;

                Vector3 endVector = Quaternion.Euler(0f, angle * 0.5f, 0f) * transform.forward;
                Vector3[] vectors = new Vector3[] { transform.position + startVector * radius, transform.position, transform.position, transform.position + endVector * radius };
                Handles.DrawAAPolyLine(debugLineThickness, vectors);
            }

            Handles.DrawWireArc(transform.position, Vector3.up, startVector, angle, radius, debugLineThickness);

            //Handles.color = Handles.color.ToWithA(0.15f);
            //Handles.DrawWireArc(transform.position, Vector3.up, startVector, (360f - angle) * -1f, radius, debugLineThickness);
        }
    }

    private void DebugDrawConnections()
    {
        if (alignmentAngle > 0f && cohesionRadius > 0f && neighborsToAllign != null)
        {
            Handles.color = Color.red;
            foreach (var other in neighborsToAllign)
            {
                Vector3 vector = other.transform.position - transform.position;
                float angle = Vector3.SignedAngle(transform.forward, vector, Vector3.up);
                if (Mathf.Abs(angle) >= alignmentAngle * 0.5f)
                {
                    continue;
                }
                Handles.DrawDottedLine(transform.position, other.transform.position, 3f);
            }
        }
    }

    private void DebugDrawDirections()
    {
        if (neighborsToAllign == null)
        {
            return;
        }
        const float lenght = 2f;
        Handles.color = Color.green;
        Handles.DrawLine(transform.position, transform.position + transform.forward * lenght);
        Handles.color = Color.blue;
        foreach (var other in neighborsToAllign)
        {
            Handles.DrawLine(other.transform.position, other.transform.position + transform.forward * lenght);
        }
    }

    private void DebugDrawLookPoints(Vector2 vector, float lerpT)
    {
        Handles.color = Color.Lerp(Color.red, Color.blue, lerpT);//Color.magenta;
        Handles.DrawSolidDisc(vector.ToVector3XZ(), Vector3.up, 0.02f);
    }

    private void DebugDrawCenterOfMass()
    {
        if (neighborsToAllign.Count == 0)
        {
            return;
        }

        Handles.color = Color.black;
        Handles.DrawSolidDisc(centerOfMass, Vector3.up, 0.2f);
    }
#endif

    #region Unity Callsbacks
    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void OnValidate()
    {
        Awake();
        cohesionTrigger.radius = cohesionRadius;
    }

    void OnEnable()
    {
        cohesionTrigger.onEnter += OnEnterCohesion;
        cohesionTrigger.onExit += OnExitCohesion;
        alignmentTrigger.onEnter += OnEnterAlignment;
        alignmentTrigger.onExit += OnExitAlignment;
        seperationTrigger.onEnter += OnEnterSeperation;
        seperationTrigger.onExit += OnExitSeperation;
    }

    void OnDisable()
    {
        cohesionTrigger.onEnter -= OnEnterCohesion;
        cohesionTrigger.onExit -= OnExitCohesion;
        alignmentTrigger.onEnter -= OnEnterAlignment;
        alignmentTrigger.onExit -= OnExitAlignment;
        seperationTrigger.onEnter -= OnEnterSeperation;
        seperationTrigger.onExit -= OnExitSeperation;
    }

    void Start()
    {
        // randomize start rotation
        if (randomiseStartPosition)
        {
            RandomizeRotation();
        }
    }

    void Update()
    {
        velocity = (transform.position - lastFramePosition) / lastDeltaTime;
        lastDeltaTime = Time.deltaTime;
        lastFramePosition = transform.position;

        // Calculate the combined steering force from cohesion, separation, and alignment

        cohesion = Cohesion();
        separation = Separation();
        alignment = Alignment();
        Vector3 steeringForce = Vector3.zero;
        steeringForce = ObstacleDetection();//FindUnobstructedDirection();
        steeringForce += cohesion * cohesionWeight;
        steeringForce += separation * separationWeight;
        steeringForce += alignment * alignmentWeight;

        //steeringForce += forwardDirection * forwardWeight;

        // Limit the steering force to the maximum force
        steeringForce = Vector3.ClampMagnitude(steeringForce, maxForce);

        // Apply the steering force to the zombie's position using agent.Move()
        Vector3 desiredVelocity = velocity + steeringForce * Time.deltaTime;
        desiredVelocity = Vector3.ClampMagnitude(desiredVelocity, maxSpeed);
        Vector3 deltaPosition = desiredVelocity * Time.deltaTime;
        agent.Move(deltaPosition);
        if (velocity.magnitude > 0.1f)
        {
            transform.forward = velocity.normalized;
        }
    }
    #endregion

    #region Callbacks
    void OnEnterCohesion(Boid neighbor)
    {
        /// TODO: check object angle all the time, not just on Enter
        /*
        Vector3 vector = neighbor.transform.position - transform.position;
        float angle = Vector3.SignedAngle(transform.forward, vector, Vector3.up);
        if (Mathf.Abs(angle) >= cohesionAngle * 0.5f)
        {
            return;
        }
        */

        if (neighborsToCohese.Contains(neighbor))
        {
            return;
        }

        neighborsToCohese.Add(neighbor);
    }

    void OnExitCohesion(Boid neighbor)
    {
        neighborsToCohese.Remove(neighbor);
    }

    void OnEnterAlignment(Boid neighbor)
    {
        /// TODO: check object angle all the time, not just on Enter
        /*
        Vector3 vector = neighbor.transform.position - transform.position;
        float angle = Vector3.SignedAngle(transform.forward, vector, Vector3.up);
        if (Mathf.Abs(angle) >= alignmentAngle * 0.5f)
        {
            return;
        }
        */

        if (neighborsToAllign.Contains(neighbor))
        {
            return;
        }

        neighborsToAllign.Add(neighbor);
    }

    void OnExitAlignment(Boid neighbor)
    {
        neighborsToAllign.Remove(neighbor);
    }

    void OnEnterSeperation(Boid neighbor)
    {
        if (neighborsToSeperate.Contains(neighbor))
        {
            return;
        }

        neighborsToSeperate.Add(neighbor);
    }

    void OnExitSeperation(Boid neighbor)
    {
        neighborsToSeperate.Remove(neighbor);
    }
    #endregion

    [ContextMenu("Ramdonize Rotation")]
    private void RandomizeRotation()
    {
        transform.Rotate(Vector3.up, Random.Range(0f, 360f));
    }

    // Calculate the average position of nearby boids and steer towards it
    private Vector3 Cohesion()
    {
        centerOfMass = Vector3.zero;

        foreach (Boid neighbor in neighborsToCohese)
        {
            centerOfMass += neighbor.transform.position;
        }

        if (neighborsToCohese.Count > 0)
        {
            centerOfMass /= neighborsToCohese.Count;
            return Seek(centerOfMass);
        }

        return Vector3.zero;
    }

    // Align the boid's velocity with the average velocity of nearby boids
    private Vector3 Alignment()
    {
        Vector3 averageVelocity = Vector3.zero;

        foreach (Boid neighbor in neighborsToAllign)
        {
            averageVelocity += neighbor.velocity;
        }

        if (neighborsToAllign.Count > 0)
        {
            averageVelocity /= neighborsToAllign.Count;
            return averageVelocity.normalized;
        }

        return Vector3.zero;
    }

    // Keep a minimum distance from other nearby boids to avoid crowding
    private Vector3 Separation()
    {
        Vector3 separationForce = Vector3.zero;

        foreach (Boid neighbor in neighborsToSeperate)
        {
            Vector3 separationVector = transform.position - neighbor.transform.position;
            float distance = separationVector.magnitude;

            if (distance > 0f)  // Avoid division by zero
            {
                separationForce += separationVector.normalized / distance;
            }
        }

        return separationForce;
    }

    // Helper function to steer the boid towards a target position
    private Vector3 Seek(Vector3 targetPosition)
    {
        Vector3 desiredVelocity = targetPosition - transform.position;
        desiredVelocity = desiredVelocity.normalized * maxSpeed;
        desiredVelocity -= velocity;
        return desiredVelocity;
    }

    private Vector3 ObstacleDetection()
    {
        const int rays = 8;

        Vector3 vector = Vector3.zero;
        for (int i = 0; i < rays; i++)
        {
            float angle = i * (360f / rays);

            Ray ray = new Ray(transform.position, Quaternion.Euler(Vector3.up * angle) * Vector3.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, cohesionRadius, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                Debug.DrawLine(ray.origin, hit.point, Color.red);

                vector += -1f * (ray.direction.magnitude / cohesionRadius) * ray.direction.normalized;
            }
            else
            {
                Debug.DrawRay(ray.origin, ray.direction * cohesionRadius, Color.blue);
            }
        }
        return vector;
    }

    private Vector3 FindUnobstructedDirection()
    {
        Vector3 bestDir = transform.forward;
        float furthestUnobstructedDst = 0;
        RaycastHit hit;
        float debugAlpha = 0.01f;

        List<Vector2> rayDirections = LookAround(null);

        for (int i = 0; i < rayDirections.Count; i++)
        {
            // tranform form local to world space so that smaller dir changes are examined first
            Vector3 dir = transform.TransformDirection(rayDirections[i].ToVector3XZ());
            dir.Normalize();
            if (Physics.SphereCast(transform.position, viewSphereCastRadius, dir, out hit, cohesionRadius, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance > furthestUnobstructedDst)
                {
                    bestDir = dir;
                    furthestUnobstructedDst = hit.distance;
                    Debug.DrawRay(transform.position, bestDir, Color.red.ToWithA(debugAlpha), 1f);
                }
            }
            else
            {
                // no obstacle in view radius, so return this direction
                Debug.DrawRay(transform.position, bestDir, Color.green.ToWithA(debugAlpha), 1f);
                return dir;
            }
        }
        // obstacles all around, so return dir where obstacle is furthest away
        Debug.DrawRay(transform.position, bestDir, Color.blue.ToWithA(debugAlpha), 1f);
        return bestDir;
    }

    private List<Vector2> LookAround(UnityAction<Vector2, float> callback)
    {
        int numPoints = 360;
        float angle = 360f / (float)numPoints;
        List<Vector2> directions = new();
        for (int i = 0; i < numPoints / 2; i++)
        {
            if (Mathf.Abs((angle * i) - 180f) < ((360f - alignmentAngle) * 0.5f))
            {
                continue;
            }

            float x = (angle * i).Sin();
            float y = (angle * i).Cos();
            Vector2 vector = new Vector2(x, y) * cohesionRadius;

            float lerpT = i / (float)(numPoints / 2);
            //float lerpT = Vector2.Distance(Vector2.up * viewRadius, vector) / viewRadius * 0.5f;
            //float lerpT = (-y + 1f) / 2f;

            callback?.Invoke(vector, lerpT);
            directions.Add(vector);

            if (angle * i > 0f && angle * i < 360f)
            {
                x = (angle * -i).Sin();
                y = (angle * -i).Cos();
                vector = new Vector2(x, y) * cohesionRadius;

                callback?.Invoke(vector, lerpT);
                directions.Add(vector);
            }
        }
        return directions;
    }
    /*
    private void LookAround_GoldenRatio(UnityAction<Vector2, float> callback)
    {
        int numPoints = 200;
        float pow = 0.5f;
        float turnFraction = 0.61803398875f; // golden ratio

        for (int i = 0; i < numPoints; i++)
        {
            float distance = Mathf.Pow(i / (numPoints - 1f), pow) * visionRadius;
            float angle = 2f * Mathf.PI * turnFraction * i;
            print(angle);

            float x = distance * Mathf.Cos(angle);
            float y = distance * Mathf.Sin(angle);
            Vector2 vector = new(x, y);

            //float lerpT = i / (float)numPoints);
            float lerpT = Vector2.Distance(Vector2.up * visionRadius, vector) / visionRadius * 0.5f;
            //float lerpT = (-y + visionRadius) / (visionRadius * 2f);

            callback.Invoke(vector, lerpT);
        }
    }
    */
}
