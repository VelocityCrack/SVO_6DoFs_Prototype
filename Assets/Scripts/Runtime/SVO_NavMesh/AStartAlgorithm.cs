using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
//using static UnityEditor.Experimental.GraphView.GraphView;
using Random = UnityEngine.Random;

public class AStartAlgorithm : MonoBehaviour 
{
    protected GameObject SVO_Constructor_GO;
    protected SVO_DataStorage SVO_DataStorage;
    protected SVO_Constructor SVO_Constructor;
    private SortedSet<Node> _openNodes;
    private HashSet<Node> _closedNodes;

    private Node _startNode;
    private Node _destinationNode; public Node Destination() => _destinationNode;

    public float Speed = 5f;
    public float Accuracy = 1f;
    public float TurnSpeed = 5f;
    [Range(0, 1)] public float VoxelProportionRadomness = 0.5f;
    [HideInInspector]public bool HasPath;
    public bool ShowGizmos = true;

    protected int currentWaypoint;
    protected Vector3 destinationPos;
    protected Node currentNode = new(); public Node CurrentNode => currentNode;

    public List<Node> path = new(); public int GetPathLength() => path.Count;
    public Node GetPathNode(int index)
    {
        if (path == null) return null;

        if (index < 0 || index >= path.Count)
        {
            Debug.LogError("Index out of bounds. Path Length: " + path.Count);
            return null;
        }
        return path[index];
    }

    public int MaxIterations = 1000;
    [Min(1)] public int PathTryCount = 5; //Número de intentos para encontrar un camino en un frame. Si no se encuentra, se intentará el próximo frame.

    public class Node
    {
        public float f, g, h;
        public Node previous;
        [SerializeReference]
        public SVO_NodeData voxelRef;
        public Vector3 position;

        public override bool Equals(object obj) => obj is Node other && voxelRef.Link == other.voxelRef.Link;
        public override int GetHashCode() => voxelRef.Link.GetHashCode();
    }

    protected virtual void Start()
    {
        SVO_Constructor_GO = IA_Director.Instance.SVO_Constructor_GO;
        SVO_Constructor = SVO_Constructor_GO.GetComponent<SVO_Constructor>();
        SVO_DataStorage = SVO_Constructor.SVO_DataStorage_SO;

        if (SVO_Constructor == null || SVO_DataStorage.SVO == null) return;
        GetClosestNode();
    }

    public bool AStar(Vector3 startPos, Vector3 destinationPos)     //Método que calcula el camino utilizando A* entre 2 posiciones dentro del SVO (malla de navegación).
    {
        path.Clear();
        if (SVO_DataStorage.SVO == null) return false;
        
        _startNode = new()
        {
            voxelRef = SVO_ContructorHelper.NodeSearch(startPos, ref SVO_DataStorage.SVO, SVO_Constructor.RootSize, ref SVO_DataStorage.position),
        }; 

        _destinationNode = new()
        {
            voxelRef = SVO_ContructorHelper.NodeSearch(destinationPos, ref SVO_DataStorage.SVO, SVO_Constructor.RootSize, ref SVO_DataStorage.position),
        }; 

        if (_startNode.voxelRef == null || _destinationNode.voxelRef == null || _startNode.voxelRef.TagZone == byte.MaxValue || _destinationNode.voxelRef.TagZone == byte.MaxValue)
        {
            //Debug.LogError("Posiciones no válidas para el A*. No están dentro del SVO o son puntos obstruidos");
            return false;
        }

        _startNode.position = SVO_ContructorHelper.GetNodeGlobalPosition(_startNode.voxelRef, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);
        _destinationNode.position = SVO_ContructorHelper.GetNodeGlobalPosition(_destinationNode.voxelRef, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);

        if (_startNode.Equals(_destinationNode))
        {
            _destinationNode.position = destinationPos;
            _destinationNode.previous = _startNode;
            ReconstructPath(_destinationNode, destinationPos);
            return true;
        }

        _openNodes = new(new NodeComparer());
        _closedNodes = new();
        int iterationCount = 0;

        _startNode.g = 0;
        _startNode.h = Heuristic(_startNode, _destinationNode);
        _startNode.f = _startNode.g + _startNode.h;
        _startNode.previous = null;
        _openNodes.Add(_startNode);

        
        while (_openNodes.Count > 0)
        {
            if (++iterationCount > MaxIterations)
            {
                //Debug.LogError("Path creation exceded max number of iterations");
                return false;
            }

            Node current = _openNodes.First();
            _openNodes.Remove(current);

            if (current.Equals(_destinationNode))
            {
                ReconstructPath(current, destinationPos);
                return true;
            }

            _closedNodes.Add(current);

            foreach (SVO_NodeData node in current.voxelRef.Neighbours)
            {
                Node neighbour = new()
                {
                    voxelRef = node                    
                };

                if (_closedNodes.Contains(neighbour)) continue;

                if (neighbour.voxelRef.FirstChild == null)      //Si el nodo tiene hijos (está bloqueado o semi bloqueado) tenemos que procesar los 4 nodos
                {
                    neighbour.position = SVO_ContructorHelper.GetNodeGlobalPosition(node, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);

                    float tentative_GScore = current.g + Heuristic(current, neighbour);

                    if (_openNodes.TryGetValue(neighbour, out Node existingNode))   //Apaño para el problema crear una clase
                    {
                        neighbour.g = existingNode.g;
                    }

                    if (tentative_GScore < neighbour.g || !_openNodes.Contains(neighbour))
                    {
                        neighbour.g = tentative_GScore;
                        neighbour.h = Heuristic(neighbour, _destinationNode);
                        neighbour.f = neighbour.g + neighbour.h;
                        neighbour.previous = current;

                        _openNodes.Add(neighbour);
                    }
                }
                else
                {
                    int x = (int)(MortonCode.DecodeMorton3X(neighbour.voxelRef.Link) - MortonCode.DecodeMorton3X(current.voxelRef.Link));

                    int[] childrenToCheck = new int[4];

                    if (x != 0)         //Aquí comprobamos en cual de los ejes y en qué sentido (+x, -x, +y, -y, +z, -z) está el vecino. Como utilizamos UINT, no hay números negativos, así que al restar 2 números si da menos de 0, se convierte en un número enorme.
                    {
                        if (x < 2)      //Positivo (Es 1)
                        {
                            childrenToCheck[0] = 0; childrenToCheck[1] = 2; childrenToCheck[2] = 4; childrenToCheck[3] = 6;
                        }
                        else           //Negativo (Es un número enorme, como 2^32)
                        {
                            childrenToCheck[0] = 1; childrenToCheck[1] = 3; childrenToCheck[2] = 5; childrenToCheck[3] = 7;
                        }
                    }
                    else
                    {
                        uint y = MortonCode.DecodeMorton3Y(neighbour.voxelRef.Link) - MortonCode.DecodeMorton3Y(current.voxelRef.Link);

                        if (y != 0)
                        {
                            if (y < 2)
                            {
                                childrenToCheck[0] = 0; childrenToCheck[1] = 1; childrenToCheck[2] = 2; childrenToCheck[3] = 3;
                            }
                            else
                            {
                                childrenToCheck[0] = 4; childrenToCheck[1] = 5; childrenToCheck[2] = 6; childrenToCheck[3] = 7;
                            }
                        }
                        else
                        {
                            uint z = MortonCode.DecodeMorton3Z(neighbour.voxelRef.Link) - MortonCode.DecodeMorton3Z(current.voxelRef.Link);

                            if (z < 2)
                            {
                                childrenToCheck[0] = 0; childrenToCheck[1] = 1; childrenToCheck[2] = 4; childrenToCheck[3] = 5;
                            }
                            else
                            {
                                childrenToCheck[0] = 2; childrenToCheck[1] = 3; childrenToCheck[2] = 6; childrenToCheck[3] = 7;
                            }
                        }
                    }

                    CheckHigherResolutionNeighbours(current, neighbour, ref childrenToCheck);
                }
            }
        }

        //Debug.LogError("Not Path Found");
        return false;
    }


    /* Comprueba los 4 hijos del vecino adyacentes a su cara del voxel para ver si son transitables (están subdivididos, aka, tienen hijos). Si uno de ellos no es transitable, vuelve a hacer este proceso, comprobando los hijos adyacentes.
     * Así hasta dar con un voxel transitable o llegar a una hoja no transitable. */

    private void CheckHigherResolutionNeighbours(Node current, Node neighbour, ref int[] childrenToCheck)
    {
        if (neighbour.voxelRef.TagZone == byte.MaxValue) return;    //Nunca debería de pasar, pero por si acaso.

        int arrayPos = Array.BinarySearch(SVO_DataStorage.SVO[SVO_ContructorHelper.GetLayer(neighbour.voxelRef.FirstChild)], neighbour.voxelRef.FirstChild.Link, SVO_NodeDataComparer.SortNodeLink());
        
        for (int i = 0; i < 4; i++)
        {
            Node neighbourChild = new()
            {
                voxelRef = SVO_DataStorage.SVO[SVO_ContructorHelper.GetLayer(neighbour.voxelRef.FirstChild)][arrayPos + childrenToCheck[i]]
            };
        
            if (_closedNodes.Contains(neighbourChild)) continue;
            if (neighbourChild.voxelRef.TagZone == byte.MaxValue) continue;
            if (neighbourChild.voxelRef.FirstChild != null)
            {
                CheckHigherResolutionNeighbours(current, neighbourChild, ref childrenToCheck);
                continue;
            }
        
            neighbourChild.position = SVO_ContructorHelper.GetNodeGlobalPosition(neighbourChild.voxelRef, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);
            float tentative_GScore = current.g + Heuristic(current, neighbourChild);

            if (_openNodes.TryGetValue(neighbourChild, out Node existingNode))   //Apaño para el problema crear una clase
            {
                neighbourChild.g = existingNode.g;
            }

            if (tentative_GScore < neighbourChild.g || !_openNodes.Contains(neighbourChild))
            {
                neighbourChild .g = tentative_GScore;
                neighbourChild.h = Heuristic(neighbourChild, _destinationNode);
                neighbourChild.f = neighbourChild.g + neighbourChild.h;
                neighbourChild.previous = current;

                _openNodes.Add(neighbourChild);
            }
        }
    }

    public bool AStarLimitedByLayer(Vector3 startPos, Vector3 destinationPos, int layer)     //Método que calcula el camino utilizando A* entre 2 posiciones dentro del SVO (malla de navegación).
    {
        path.Clear();
        if (SVO_DataStorage.SVO == null) return false;

        _startNode = new()
        {
            voxelRef = SVO_ContructorHelper.NodeSearch(startPos, ref SVO_DataStorage.SVO, SVO_Constructor.RootSize, ref SVO_DataStorage.position, layer),
        }; 

        _destinationNode = new()
        {
            voxelRef = SVO_ContructorHelper.NodeSearch(destinationPos, ref SVO_DataStorage.SVO, SVO_Constructor.RootSize, ref SVO_DataStorage.position, layer),
        }; 

        if (_startNode.voxelRef == null || _destinationNode.voxelRef == null || _startNode.voxelRef.TagZone == byte.MaxValue || _destinationNode.voxelRef.TagZone == byte.MaxValue)
        {
            //Debug.LogError("Posiciones no válidas para el A*. No están dentro del SVO o son puntos obstruidos");
            return false;
        }

        _startNode.position = SVO_ContructorHelper.GetNodeGlobalPosition(_startNode.voxelRef, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);
        _destinationNode.position = SVO_ContructorHelper.GetNodeGlobalPosition(_destinationNode.voxelRef, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);

        if (_startNode.Equals(_destinationNode))
        {
            _destinationNode.position = destinationPos;
            _destinationNode.previous = _startNode;
            ReconstructPath(_destinationNode, destinationPos);
            return true;
        }

        _openNodes = new(new NodeComparer());
        _closedNodes = new();
        int iterationCount = 0;

        _startNode.g = 0;
        _startNode.h = Heuristic(_startNode, _destinationNode);
        _startNode.f = _startNode.g + _startNode.h;
        _startNode.previous = null;
        _openNodes.Add(_startNode);


        while (_openNodes.Count > 0)
        {
            if (++iterationCount > MaxIterations)
            {
                //Debug.LogError("Path creation exceded max number of iterations");
                return false;
            }

            Node current = _openNodes.First();
            _openNodes.Remove(current);

            if (current.Equals(_destinationNode))
            {
                ReconstructPath(current, destinationPos);
                return true;
            }

            _closedNodes.Add(current);

            foreach (SVO_NodeData node in current.voxelRef.Neighbours)
            {
                Node neighbour = new()          //EL CREAR UNA CLASE CADA QUE SE QUIERE COMPROBAR UN VECINO PUEDE AFECTAR LA COMPRABACIÓN DE LA SORTEDLIST YA QUE NEIGHTBOUR.G SIEMPRE VA A SER 0.
                {
                    voxelRef = node
                };

                if (_closedNodes.Contains(neighbour)) continue;

                if (neighbour.voxelRef.FirstChild == null)      //Si el nodo tiene hijos (está bloqueado o semi bloqueado) tenemos que procesar los 4 nodos
                {
                    neighbour.position = SVO_ContructorHelper.GetNodeGlobalPosition(node, SVO_Constructor.RootSize, SVO_DataStorage.SVO.Length, ref SVO_DataStorage.position);

                    float tentative_GScore = current.g + Heuristic(current, neighbour);
                    
                    if (_openNodes.TryGetValue(neighbour, out Node existingNode))   //Apaño para el problema crear una clase
                    {
                        neighbour.g = existingNode.g;   
                    }
                    if (tentative_GScore < neighbour.g || !_openNodes.Contains(neighbour))
                    {
                        neighbour.g = tentative_GScore;
                        neighbour.h = Heuristic(neighbour, _destinationNode);
                        neighbour.f = neighbour.g + neighbour.h;
                        neighbour.previous = current;

                        _openNodes.Add(neighbour);
                    }
                }
                else if (SVO_ContructorHelper.GetLayer(neighbour.voxelRef.FirstChild.Link) >= layer)
                {
                    int x = (int)(MortonCode.DecodeMorton3X(neighbour.voxelRef.Link) - MortonCode.DecodeMorton3X(current.voxelRef.Link));

                    int[] childrenToCheck = new int[4];

                    if (x != 0)         //Aquí comprobamos en cual de los ejes y en qué sentido (+x, -x, +y, -y, +z, -z) está el vecino. Como utilizamos UINT, no hay números negativos, así que al restar 2 números si da menos de 0, se convierte en un número enorme.
                    {
                        if (x < 2)      //Si es mayor que 1 (positivo)      
                        {
                            childrenToCheck[0] = 0; childrenToCheck[1] = 2; childrenToCheck[2] = 4; childrenToCheck[3] = 6;
                        }
                        else           //Si es menor que 1 (negativo)
                        {
                            childrenToCheck[0] = 1; childrenToCheck[1] = 3; childrenToCheck[2] = 5; childrenToCheck[3] = 7;
                        }
                    }
                    else
                    {
                        uint y = MortonCode.DecodeMorton3Y(neighbour.voxelRef.Link) - MortonCode.DecodeMorton3Y(current.voxelRef.Link);

                        if (y != 0)
                        {
                            if (y < 2)
                            {
                                childrenToCheck[0] = 0; childrenToCheck[1] = 1; childrenToCheck[2] = 2; childrenToCheck[3] = 3;
                            }
                            else
                            {
                                childrenToCheck[0] = 4; childrenToCheck[1] = 5; childrenToCheck[2] = 6; childrenToCheck[3] = 7;
                            }
                        }
                        else
                        {
                            uint z = MortonCode.DecodeMorton3Z(neighbour.voxelRef.Link) - MortonCode.DecodeMorton3Z(current.voxelRef.Link);

                            if (z < 2)
                            {
                                childrenToCheck[0] = 2; childrenToCheck[1] = 3; childrenToCheck[2] = 6; childrenToCheck[3] = 7;
                            }
                            else
                            {
                                childrenToCheck[0] = 0; childrenToCheck[1] = 1; childrenToCheck[2] = 4; childrenToCheck[3] = 5;
                            }
                        }
                    }

                    CheckHigherResolutionNeighbours(current, neighbour, ref childrenToCheck);
                }
            }
        }

        Debug.LogError("Not Path Found");
        return false;
    }

    public class NodeComparer : IComparer<Node>     //Comparador para el SortedSet (openNodes). Compara en base al valor f de cada nodo para procesar antes aquellos más cercanos a la meta.
    {
        public int Compare(Node x, Node y)
        {
            if (x == null || y == null)
            {
                return 0;
            } 

            int compare = x.f.CompareTo(y.f);
            if (compare == 0)
            {
                return x.voxelRef.Link.CompareTo(y.voxelRef.Link);  //Esto está un poco porque sí
            }
            return compare;
        }
    }

    private void ReconstructPath(Node current, Vector3 destinationPosition)      //Reconstruye el camino cogiendo el nodo destino y llamando recursivamente a su nodo registrado como previo. Luego se da la vuelta a la lista y tenemos camino.
    {
        Node trueDestination = new()
        {
            voxelRef = _destinationNode.voxelRef,
            position = destinationPosition
        };

        path.Add(trueDestination);

        while (current != null)
        {
            path.Add(current);
            current = current.previous;
        }

        path.Reverse();

        currentWaypoint = 0;
        currentNode = GetPathNode(0);
        destinationPos = currentNode.position + SVO_ContructorHelper.GetVoxelSize(currentNode.voxelRef, SVO_Constructor.RootSize, SVO_Constructor.NumLayers) / 2 * VoxelProportionRadomness * Random.insideUnitSphere;

        HasPath = true;
    }

    private void GetClosestNode()
    {
        currentNode.voxelRef = SVO_ContructorHelper.NodeSearch(transform.position, ref SVO_DataStorage.SVO, SVO_Constructor.RootSize, ref SVO_Constructor.SVO_GlobalPosition);
    }

    private float Heuristic(Node a, Node b) => (a.position - b.position).sqrMagnitude;      //Utilizo la distancia entre los nodos al cuadrado porque es una operación menos (no hace la raíz cuadrada)
    //Debería de cambiar la heurística para que sea +1 y así que tenga igual preferencia los nodos grandes y pequeños.

    protected void GoToRandomDestination()
    {
        if (SVO_DataStorage.SVO == null) return;

        for (int i = 0; i < PathTryCount; i++)
        {
            destinationPos = new Vector3(Random.Range(0, SVO_Constructor.RootSize), Random.Range(0, SVO_Constructor.RootSize), Random.Range(0, SVO_Constructor.RootSize)) + SVO_DataStorage.position;

            if (AStar(transform.position, destinationPos))
            {
                return;
            }
        }

        //do
        //{
        //    destinationPos = new Vector3(Random.Range(0, SVO_Constructor.rootSize), Random.Range(0, SVO_Constructor.rootSize), Random.Range(0, SVO_Constructor.rootSize)) + SVO_DataStorage.position;
        //}
        //while (!AStar(transform.position, destinationPos));
    }

    protected void GoToRandomDestination(int layer)
    {
        if (SVO_DataStorage.SVO == null) return;

        for (int i = 0; i < PathTryCount; i++)
        {
            destinationPos = new Vector3(Random.Range(0, SVO_Constructor.RootSize), Random.Range(0, SVO_Constructor.RootSize), Random.Range(0, SVO_Constructor.RootSize)) + SVO_DataStorage.position;

            if (AStarLimitedByLayer(transform.position, destinationPos, layer))
            {
                return;
            }
        }

        //do
        //{
        //    print("Trying To Go To Random Destination");
        //    destinationPos = new Vector3(Random.Range(0, SVO_Constructor.rootSize), Random.Range(0, SVO_Constructor.rootSize), Random.Range(0, SVO_Constructor.rootSize)) + SVO_DataStorage.position;
        //}
        //while (!AStarLimitedByLayer(transform.position, destinationPos, layer));
        //currentWaypoint = 0;
    }

    protected bool GoToDestination(Vector3 destination)
    {
        if (SVO_DataStorage.SVO == null) return false;

        destinationPos = destination;

        if (AStar(transform.position, destinationPos))
        {
            //currentWaypoint = 0;
            return true;
        }
        else
        {
            return false;
        }
    }

    protected bool GoToDestination(Vector3 destination, int layer)
    {
        if (SVO_DataStorage.SVO == null) return false;

        destinationPos = destination;

        if (AStarLimitedByLayer(transform.position, destinationPos, layer))
        {
            //currentWaypoint = 0;
            return true;
        }
        else
        {
            return false;
        }
    }


    protected virtual void OnDrawGizmos()
    {
        if (!ShowGizmos || SVO_Constructor == null || SVO_DataStorage.SVO == null || GetPathLength() == 0) return;

        if (SVO_Constructor.ShowPathsDetailed)
        {
            for (int i = 0; i < GetPathLength(); i++)
            {
                Gizmos.color = SVO_Constructor.GizmosColor[SVO_ContructorHelper.GetLayer(GetPathNode(i).voxelRef)];
                Gizmos.DrawWireCube(SVO_ContructorHelper.GetNodeGlobalPosition(GetPathNode(i).voxelRef, SVO_Constructor.RootSize, SVO_Constructor.NumLayers, ref SVO_Constructor.SVO_GlobalPosition), 0.95f * SVO_ContructorHelper.GetVoxelSize(GetPathNode(i).voxelRef, SVO_Constructor.RootSize, SVO_Constructor.NumLayers) * Vector3.one);
            }
        }

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(GetPathNode(0).position, 0.7f);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetPathNode(GetPathLength() - 1).position, 0.7f);

        Gizmos.color = Color.green;
        for (int i = 0; i < GetPathLength(); i++)
        {
            Gizmos.DrawWireSphere(GetPathNode(i).position, 0.5f);

            if (i < GetPathLength() - 1)
            {
                Vector3 start = GetPathNode(i).position;
                Vector3 end = GetPathNode(i + 1).position;
                Gizmos.DrawLine(start, end);
            }
        }
    }
}
