using UnityEngine;

public class SVO_Agent : AStartAlgorithm
{
    [Tooltip("The size of the Agent in meters. Set it as -1 if it can navigate through all the SVO layers.")]
    [Min(-1)] public int Size = -1;
    public LayerMask AgentLayerMask;
    protected int realAgentLayerMask;
    protected int _SVO_Layer = -1;

    protected bool autoRotation = true;
    protected bool autoMovement = true;

    protected override void Start()
    {
        base.Start();
        realAgentLayerMask = (int)Mathf.Log(AgentLayerMask.value, 2);
        gameObject.layer = realAgentLayerMask;

        for (int i = 0; i < SVO_DataStorage.numLayers; i++)
        {
            if (Size <= Mathf.Pow(2, SVO_DataStorage.leafSize + i))
            {
                _SVO_Layer = i;
                break;
            }
        }
    }

    protected void Update()
    {
        if (SVO_Constructor == null || SVO_DataStorage.SVO == null) return;

        if (GetPathLength() == 0 || currentWaypoint >= GetPathLength())     //If has path
        {
            HasPath = false;
            GoToRandomDestination(_SVO_Layer);
            return;
        }

        if (Vector3.Distance(destinationPos, transform.position) < Accuracy)        //If reaches the next waypoint
        {
            //Debug.Log($"Waypoint {currentWaypoint} reached");
            currentWaypoint++;

            if (currentWaypoint < GetPathLength())      //If there are more waypoints to go
            {
                currentNode = GetPathNode(currentWaypoint);
                if (currentWaypoint == 0 || currentWaypoint == GetPathLength() - 1)
                {
                    destinationPos = currentNode.position;
                }
                else
                {
                    destinationPos = currentNode.position + SVO_ContructorHelper.GetVoxelSize(currentNode.voxelRef, SVO_Constructor.RootSize, SVO_Constructor.NumLayers) / 2 * VoxelProportionRadomness * Random.insideUnitSphere;  //Genera una posición aleatoria cercana (dentro del vóxel) a la posición del siguiente punto de la ruta.
                }       
            }
            else
            {
                return;
            }
        }

        Vector3 dir = (destinationPos - transform.position).normalized;

        if (autoRotation)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), TurnSpeed * Time.deltaTime);
        }

        if (autoMovement)
        {
            transform.Translate(0, 0, Speed * Time.deltaTime);
        }
        
    }
}
