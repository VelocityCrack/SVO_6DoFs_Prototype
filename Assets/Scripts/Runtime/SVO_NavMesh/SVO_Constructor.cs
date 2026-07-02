using NUnit.Framework;
using NUnit.Framework.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

using static TMPro.SpriteAssetUtilities.TexturePacker_JsonArray;
using static UnityEditor.PlayerSettings;
using static UnityEngine.InputSystem.Controls.AxisControl;


//using static UnityEditor.Experimental.GraphView.GraphView;
using Random = UnityEngine.Random;

public class SVO_Constructor : Singleton<SVO_Constructor>
{
    public int RootSize;
    public int NumLayers;
    public Vector3 SVO_GlobalPosition;
    [SerializeField] private LayerMask obstacleLayer; public LayerMask ObstacleLayer => obstacleLayer;

    public bool CreatingSVO;

    private SVO_DataStorage.Zones[] _zonesList;
    private List<SVO_NodeData>[] _provisionalPositionList;

    public SVO_DataStorage SVO_DataStorage_SO;

    private Stack<SVO_NodeData> _subdivisionPendingVoxels = new();
    private int _numNodes;
    private int _optimizedNodes;
    private int _totalNodes;
    private int _totalNeighbours;

    #region Pruebas

    //bool next;
    //public GameObject Cube;

    //private void Update()
    //{
    //    if (Input.GetKeyDown(KeyCode.V))
    //    {
    //        next = true;
    //    }
    //}

    //private IEnumerator Prueba()
    //{
    //    uint a = 0; 
    //    for (int i = 0; i < 259; i++)
    //    {
    //        Vector3 b = new Vector3(MortonCode.DecodeMorton3X(a), MortonCode.DecodeMorton3Y(a), MortonCode.DecodeMorton3Z(a));
    //        Instantiate(Cube, new Vector3(b.x + bounds.position.x + 0.5f, b.y + bounds.position.y + 0.5f, b.z + bounds.position.z + 0.5f), Quaternion.identity);
    //        print(b);
    //        a ++;


    //        next = false;
    //        yield return new WaitUntil(() => next);

    //    }
    //}
    #endregion

    #region SVO Creation Flow

    public void InitializeSVO()
    {
        print("Initializing SVO");

        if (SVO_DataStorage_SO == null)
        {
            print("SVO_DataStorage_SO is not assigned. Make sure to select one.");
            return;
        }

        CreatingSVO = true;

        RootSize = SVO_DataStorage_SO.rootSize;
        NumLayers = SVO_DataStorage_SO.numLayers;
        obstacleLayer = SVO_DataStorage_SO.obstacleLayer;
        SVO_GlobalPosition = SVO_DataStorage_SO.position;
        _subdivisionPendingVoxels.Clear();
        ShowSVOToogleArray = new bool[NumLayers];
        ShowNeighboursToogleArray = new bool[NumLayers - 1];

        _provisionalPositionList = new List<SVO_NodeData>[NumLayers];
        for (int i = 0; i < _provisionalPositionList.Length; i++)
        {
            _provisionalPositionList[i] = new List<SVO_NodeData>();
        }
        RootSize = (int)Mathf.Pow(2, RootSize);

        _zonesList = SVO_DataStorage_SO.zoneList;

        RootNodesGeneration();
    }

    //Se genera el root node. Se asigna el link. Se mete a SVO[0][]. Se llama a OctreSubdivision.
    private void RootNodesGeneration()
    {
        SVO_DataStorage_SO.SVO = new SVO_NodeData[NumLayers][];
        SVO_DataStorage_SO.SVO[NumLayers - 1] = new SVO_NodeData[1];

        var node = new SVO_NodeData
        {
            Link = (uint)((NumLayers - 1) << 28)
        };
        SVO_DataStorage_SO.SVO[NumLayers - 1][0] = node;
        node.TagZone = SVO_ContructorHelper.CheckTagZone(node, ref _zonesList, RootSize, NumLayers, ref SVO_GlobalPosition);

        if (SVO_ContructorHelper.CheckForCollision(node, obstacleLayer, RootSize, NumLayers, ref SVO_GlobalPosition))        //Si detecta colisión empieza la función de recursión que prufundiza en ese vóxel, subdividiéndolo hasta llegar a mayor resolución
        {
            print("Generating NavMesh");
            _subdivisionPendingVoxels.Push(node);

            InspectPendingNodes();
            SVOToArray();
            OptimizeOctree();
            GenerateNeighours();
            SVOGenerated();
        }
        else
        {
            print("Error generating NavMesh. Check the colliders are enabled, have the appropiate tag and are inside the SVO limits.");
            CreatingSVO = false;
        }
    }

    /// <summary>
    /// Mientras haya nodos pendientes de subdividir, los manda subdividir. <br/>
    /// Esta función se hace para evitar la recursión infinita y el desbordamiento de pila que podría suponer llamar a <see cref="SubdivideNode"/> recursivamente en <see cref="SubdivideNode"/> o <see cref="OctreeSubdivision"/> directamente. <br/>
    /// Empieza subidiviendo el nodo raíz, que es el único nodo que hay en el stack al principio.
    /// </summary>
    private void InspectPendingNodes()
    {
        while (_subdivisionPendingVoxels.Count != 0)
        {
            OctreeSubdivision(_subdivisionPendingVoxels.Pop());
        }
    }


    /// <summary>
    ///  Si el nodo tiene colisión, lo manda a subdividir a <see cref="SubdivideNode"/> donde genera los links de cada hijo, las relaciones hijos-padre y los TagZones. 
    /// </summary>
    private void OctreeSubdivision(SVO_NodeData node)
    {
        if (SVO_ContructorHelper.CheckForCollision(node, obstacleLayer, RootSize, NumLayers, ref SVO_GlobalPosition))        //Si detecta colisión empieza la función de recursión que profundiza en ese vóxel, subdividiéndolo hasta llegar a mayor resolución.
        {
            node.FirstChild = SubdivideNode(node);  //Manda subdividir el nodo en 8, generar los links y las relaciones hijos-padre y los tagZones.
        }
    }


    /// <summary>
    ///  <para>
    ///   Divide el voxel en 8, genera los links, las relaciones padre-hijo (el padre solo tiene referencia al primer hijo, mientras que todos los hijos tiene referencia del padre) y los TagZones. <br/>
    ///   También almacena los nodos en <see cref="_provisionalPositionList"/>, que es un array de listas, donde el índice de la posición de la lista representa la capa de los nodos en esa lista. Estas listas son provisionales para después ordenarlas y pasarlas al array del SVO. <br/>
    ///   Por otro lado, almacena los nodos cuya subdivisión no se ha evaluado en la pila <see cref="_subdivisionPendingVoxels"/> para explorarlos más tarde. <br/>
    ///   Al meterlos en la pila <see cref="_subdivisionPendingVoxels"/> en orden del 8º al 1º, se asegura que al hacer pop para subdividirlos después, se subdividen en orden del 1º al 8º. Este orden de generacíón de profundidad
    ///  </para>
    /// </summary>
    /// <remarks>
    ///  Pila: Estructura de datos que almacena los datos de tal forma que al acceder a ellos sigue el método de "último en entrar, primero en salir". Push() : Mete un dato. Peek(): Mira el dato que le toca salir. Pop(): Idem que peek, pero además lo elimina.
    /// </remarks>
    /// <param name="parentNode">El nodo que se va a subdividir.</param>
    /// <returns>El primer nodo hijo generado a partir del nodo padre.</returns>
    private SVO_NodeData SubdivideNode(SVO_NodeData parentNode)
    {
        SVO_NodeData firstChildNode = new()      //Genera el primer nodo hijo, relación hijo-padre y link.
        {
            Parent = parentNode,
            Link = SVO_ContructorHelper.GetFirstChildLinkFromParent(parentNode)
        };

        parentNode.FirstChild = firstChildNode;     //Genera relación padre-hijo
        _provisionalPositionList[SVO_ContructorHelper.GetLayer(firstChildNode)].Add(firstChildNode);

        if (SVO_ContructorHelper.GetLayer(firstChildNode) != 0)          //Si NO son nodos hoja
        {
            firstChildNode.TagZone = SVO_ContructorHelper.CheckTagZone(firstChildNode, ref _zonesList, RootSize, NumLayers, ref SVO_GlobalPosition);  //TagZone del primer hijo si NO es hoja
            for (int i = 7; i > 0; i--)     //Crea los demás hijos y los guarda para después revisarlos. Además, asigna la TagZone y las relaciones hijos-padre.
            {
                SVO_NodeData node = new()
                {
                    Parent = parentNode,
                    Link = firstChildNode.Link + (uint)i,
                };

                node.TagZone = SVO_ContructorHelper.CheckTagZone(node, ref _zonesList, RootSize, NumLayers, ref SVO_GlobalPosition);
                _subdivisionPendingVoxels.Push(node);
                _provisionalPositionList[SVO_ContructorHelper.GetLayer(firstChildNode)].Add(node);      //Guarda el nodo en la lista provisional en su capa para después ordenarlos y pasarlos al array del SVO.
            }
            _subdivisionPendingVoxels.Push(firstChildNode);      //Los pushea del 8º al 1º para que al hacer pop salgan en orden.
        }

        else        //Si son nodos hoja
        {
            if (SVO_ContructorHelper.CheckForCollision(firstChildNode, obstacleLayer, RootSize, NumLayers, ref SVO_GlobalPosition))      //TagZone del primer hijo si es hoja
            {
                firstChildNode.TagZone = byte.MaxValue;
            }
            else
            {
                firstChildNode.TagZone = SVO_ContructorHelper.CheckTagZone(firstChildNode, ref _zonesList, RootSize, NumLayers, ref SVO_GlobalPosition);
            }

            for (int i = 7; i > 0; i--)     //Crea las hojas, les asigna el tagZone y no los añade a la lista de nodos pendientes ya que no se pueden subdividir más.
            {
                SVO_NodeData node = new()
                {
                    Parent = parentNode,
                    Link = firstChildNode.Link + (uint)i,
                };

                if (SVO_ContructorHelper.CheckForCollision(node, obstacleLayer, RootSize, NumLayers, ref SVO_GlobalPosition))
                {
                    node.TagZone = byte.MaxValue;
                }
                else
                {
                    node.TagZone = SVO_ContructorHelper.CheckTagZone(node, ref _zonesList, RootSize, NumLayers, ref SVO_GlobalPosition);
                }

                _provisionalPositionList[0].Add(node);
            }
        }

        return firstChildNode;
    }

    /// <summary>
    ///  Ordena las listas de nodos por link y capa y las convierte en arrays para el SVO. Así, los datos están serializados y los nodos están en Morton Order en memoria.
    /// </summary> 
    private void SVOToArray()
    {
        print("Transforming and Storing NavMesh");

        _numNodes = 0;

        for (int i = 0; i < NumLayers - 1; i++)
        {
            _numNodes += _provisionalPositionList[i].Count;
            SVO_DataStorage_SO.SVO[i] = new SVO_NodeData[_provisionalPositionList[i].Count];
            SVO_DataStorage_SO.SVO[i] = _provisionalPositionList[i].ToArray();
            Array.Sort(SVO_DataStorage_SO.SVO[i], SVO_NodeDataComparer.SortNodeNode());
        }
        _provisionalPositionList = null;
    }


    /// <summary>
    ///  Deshace la subdivisión de nodos cuyos hijos están completamente bloqueados. Es decir, aquellos donde todas las sucesivas subdivisiones no han encontrado un espacio libre.<br/>
    /// </summary>
    /// <remarks>
    ///  Para ello, analiza capa por capa desde la 0, si los nodos subidividos tienen los 8 hijos bloqueados. Si es así, los borra y pone la TagZone del padre en 255.<br/>
    ///  Este proceso se propaga en el resto de capas desde abajo (las más pequeñas) hacia arriba (hasta la anterior a la raíz).
    /// </remarks>
    private void OptimizeOctree()       
    {
        print("Optimizing NavMesh");
        _optimizedNodes = 0;
        _totalNodes = 0;
        
        bool areAllChildrenOccupied = true;

        for (int i = 0; i < NumLayers - 2; i++)     //Analiza capa por capa buttom-up.
        {
            int arrayTracker = 0;
            while (arrayTracker < SVO_DataStorage_SO.SVO[i].Length)     //Analiza cada nodo de cada capa. Como las subdivisiones son de 8, se puede ir avanzando en el array de 8 en 8 para comprobar cada padre.
            {
                for (int j = 0; j < 8; j++)     //Comprueba si todos los hijos de ese padre están ocupados.
                {
                    if (SVO_DataStorage_SO.SVO[i][arrayTracker + j].TagZone != byte.MaxValue)
                    {
                        areAllChildrenOccupied = false;
                    }
                }

                if (areAllChildrenOccupied)     //Si están todos los hijos del padre ocupados, los borra, borra la refencia de primer hijo del padre y le pone la TagZone como bloqueada.
                {
                    SVO_DataStorage_SO.SVO[i][arrayTracker].Parent.TagZone = byte.MaxValue;
                    SVO_DataStorage_SO.SVO[i][arrayTracker].Parent.FirstChild = null;

                    for (int j = 0; j < 8; j++)
                    {
                        SVO_DataStorage_SO.SVO[i][arrayTracker + j] = null;
                        _optimizedNodes++;   
                    }
                }

                areAllChildrenOccupied = true;
                arrayTracker += 8;
            }
        }

        for (int i = 0; i < NumLayers - 1; i++)
        {
            SVO_DataStorage_SO.SVO[i] = SVO_DataStorage_SO.SVO[i].Where(x => x != null).ToArray();
            _totalNodes += SVO_DataStorage_SO.SVO[i].Length;
            Array.Sort(SVO_DataStorage_SO.SVO[i], SVO_NodeDataComparer.SortNodeNode());
        }
    }

    /// <summary>
    ///  Manda generar los vecinos de cada nodo del SVO de arriba (raíz) abajo (hojas), excepto la raíz, que no tiene vecinos. <br/>
    ///  Para ello, recorre cada nodo de cada capa y llama a <see cref="GetNeighbours"/> para generar su lista de vecinos.
    /// </summary>
    private void GenerateNeighours()
    {
        print("Generating Neighbours");
        _totalNeighbours = 0;

        for (int i = NumLayers - 2; i >= 0; i--)
        {
            int maxNodesInAxis = (int)Mathf.Pow(2, NumLayers - i - 1);

            for (int j = 0; j < SVO_DataStorage_SO.SVO[i].Length; j++)
            {
                if (SVO_DataStorage_SO.SVO[i][j].TagZone == byte.MaxValue) continue;
                //if (SVO_DataStorage_SO.SVO[i][j].FirstChild != null) continue;

                SVO_DataStorage_SO.SVO[i][j].Neighbours = new List<SVO_NodeData>();
                GetNeighbours(SVO_DataStorage_SO.SVO[i][j], i, maxNodesInAxis);
            }
        }
    }


    /// <summary>
    ///   Genera los vecinos de un nodo específico dentro del SVO.<br/>
    ///   Para ello, recorre las seis direcciones posibles (arriba, abajo, izquierda, derecha, adelante, atrás) y verifica si existen nodos vecinos válidos en esas posiciones.
    /// </summary>
    /// <remarks>
    ///   Si un nodo no tiene un vecino en la misma capa en una dirección se puede deber a 4 motivos y se intenta encontrar un vecino no ocupado en las capas superiores con el mismo desplazamiento.<br/>
    ///   1. No hay nodo porque esa posición está fuera de los límites del SVO. <br/>
    ///   2. Hay un nodo, pero está ocupado en esa capa. <br/> 
    ///   3. Hay un nodo libre en alguna capa superior que no se ha subdividido. <br/>
    ///   4. Hay un nodo ocupado en alguna capa superior. No está subdividido debido a la optimización. <br/>
    /// </remarks>
    /// <param name="node"> Nodo a analizar en busca de vecinos. </param>
    /// <param name="nodeLayer"> Capa del nodo dentro del SVO. </param>
    /// <param name="maxNodesInAxis"> Número máximo de nodos de un extremo a otro en cada eje. En la capa raíz siempre es 1, y en la siguiente 2, aumentando en potencias de 2. </param>
    private void GetNeighbours(SVO_NodeData node, int nodeLayer, int maxNodesInAxis)
    {
        Vector3 nodeLocalPos = SVO_ContructorHelper.GetNodeLocalPosition(node);
        Vector3 neighbourLocalPos;

        for (int i = 0; i < 6; i++)
        {
            neighbourLocalPos = GetNeighbourHypotheticalLocalPosition(nodeLocalPos, i, maxNodesInAxis);

            if (neighbourLocalPos == nodeLocalPos) { continue; }      //If the neighbour position is the same as the node position, it means that the neighbour is out of bounds, so it skips to the next neighbour.

            if (GetNodeInLayerFromLocalPosition(neighbourLocalPos, (uint)nodeLayer) is { } neighbour)     //Checks if a node exist in that position and adds it to the Neighbours list.
            {
                if (neighbour.TagZone == byte.MaxValue)
                {
                    continue;
                }

                node.Neighbours.Add(neighbour);
                _totalNeighbours++;
            }
            else if (node.Parent.Neighbours != null)
            {
                SVO_NodeData parent = node.Parent;
                int parentLayer = nodeLayer + 1;
                uint neighbourLayer; 
                Vector3 parentLocalPos;
                Vector3 parentNeighbourLocalPos;
                //int parentMaxNodesInAxis;

                Vector3 dir = i switch
                {
                    0 => Vector3.right,
                    1 => Vector3.left,
                    2 => Vector3.up,
                    3 => Vector3.down,
                    4 => Vector3.forward,
                    5 => Vector3.back,
                    _ => Vector3.zero,
                };

                foreach (var parentNeighbour in parent.Neighbours)
                {
                    parentNeighbourLocalPos = SVO_ContructorHelper.GetNodeLocalPosition(parentNeighbour);
                    neighbourLayer = SVO_ContructorHelper.GetLayer(parentNeighbour);

                    while (parentLayer < neighbourLayer)
                    {
                        parent = parent.Parent;
                        parentLayer++;
                    }

                    parentLocalPos = SVO_ContructorHelper.GetNodeLocalPosition(parent);

                    if (parentNeighbourLocalPos - parentLocalPos == dir)
                    {
                        node.Neighbours.Add(parentNeighbour);
                        _totalNeighbours++;
                    }
                }

                //while (parentLayer < NumLayers - 2)
                //{
                //    parentLocalPos = SVO_ContructorHelper.GetNodeLocalPosition(parent);
                //    parentMaxNodesInAxis = (int)Mathf.Pow(2, NumLayers - parentLayer - 1);

                //    parentNeighbourLocalPos = GetNeighbourHypotheticalLocalPosition(parentLocalPos, i, parentMaxNodesInAxis);
                //    if (parentNeighbourLocalPos == parentLocalPos)      //If the neighbour position is the same as the node position, it means that the neighbour is out of bounds, so it skips to the next neighbour.
                //    {
                //        parent = parent.Parent;
                //        parentLayer++;
                //        continue;
                //    }

                //    if (GetNodeInLayerFromLocalPosition(parentNeighbourLocalPos, (uint)parentLayer) is { } parentNeighbour)     //If a node exist in that position add it to the Neighbours list and break the loop, because it has found a neighbour in a higher layer. If it is null, go to the next parent layer and try again.
                //    {
                //        if (parentNeighbour.TagZone == byte.MaxValue || parentNeighbour.FirstChild != null)
                //        {
                //            break;
                //        }

                //        node.Neighbours.Add(parentNeighbour);
                //        break;
                //    }

                //    parent = parent.Parent;
                //    parentLayer++;
                //}
                

            //    Vector3 parentLocalPos = SVO_ContructorHelper.GetNodeLocalPosition(node.Parent);

            //    if ((i & 1) == 0)
            //    {
            //        parentLocalPos.x += (i >> 1) == 0 ? 1 : 0;
            //        parentLocalPos.y += (i & 2) == 0 ? 0 : 1;
            //        parentLocalPos.z += (i & 4) == 0 ? 0 : 1;
            //    }
            //    else
            //    {
            //        parentLocalPos.x -= (i >> 1) == 0 ? 1 : 0;
            //        parentLocalPos.y -= (i & 2) == 0 ? 0 : 1;
            //        parentLocalPos.z -= (i & 4) == 0 ? 0 : 1;
            //    }
                 
            //    if (GetNodeInLayerFromLocalPosition(parentLocalPos, (uint)nodeLayer + 1) is { } parentNeighbour)
            //    {
            //        if (parentNeighbour.TagZone == byte.MaxValue)
            //        {
            //            continue;
            //        }
            //        node.Neighbours.Add(parentNeighbour);
            //    }
            }
        }
    } 

    private Vector3 GetNeighbourHypotheticalLocalPosition(Vector3 nodeLocalPos, int direction, int maxNodesInAxis)
    {
        Vector3 nodeNeighbourLocalPos = nodeLocalPos;   
        if ((direction & 1) == 0)       //This is what decides if the number is positive or negative, alternating each time: +, -, +, -, +, -
        {
            nodeNeighbourLocalPos.x += (direction >> 1) == 0 ? 1 : 0; if (nodeNeighbourLocalPos.x >= maxNodesInAxis) { return nodeLocalPos; }
            nodeNeighbourLocalPos.y += (direction & 2) == 0 ? 0 : 1;  if (nodeNeighbourLocalPos.y >= maxNodesInAxis) { return nodeLocalPos; }   //Gets neighbour coordenates by offseting the axis. Checks in this order: +X, -X, +Y, -Y, +Z, -Z
            nodeNeighbourLocalPos.z += (direction & 4) == 0 ? 0 : 1;  if (nodeNeighbourLocalPos.z >= maxNodesInAxis) { return nodeLocalPos; }
        }
        else
        {
            nodeNeighbourLocalPos.x -= (direction >> 1) == 0 ? 1 : 0; if (nodeNeighbourLocalPos.x < 0) { return nodeLocalPos; }
            nodeNeighbourLocalPos.y -= (direction & 2) == 0 ? 0 : 1;  if (nodeNeighbourLocalPos.y < 0) { return nodeLocalPos; }
            nodeNeighbourLocalPos.z -= (direction & 4) == 0 ? 0 : 1;  if (nodeNeighbourLocalPos.z < 0) { return nodeLocalPos; }
        }
        return nodeNeighbourLocalPos;
    }

    private SVO_NodeData GetNodeInLayerFromLocalPosition(Vector3 position, uint layer)      //Debería estar en SVO_ContructorHelper
    {
        int arrayPos = Array.BinarySearch(SVO_DataStorage_SO.SVO[layer], MortonCode.EncodeMorton3((uint)position.x, (uint)position.y, (uint)position.z) + (layer << 28), SVO_NodeDataComparer.SortNodeLink());

        if (arrayPos >= 0)
        {
            return SVO_DataStorage_SO.SVO[layer][arrayPos];
        }
        else
        {
            return null;
        }
    }


    private void SVOGenerated()
    {
        CreatingSVO = false;
        print(_numNodes + " nodes created.");
        print(_optimizedNodes + " nodes optimized.");
        print(_totalNodes + " nodes in SVO.");
        print(_totalNeighbours+ " neighbours in SVO.");
        print("SVO Creation Finished.");
    }


    #endregion

    #region Gizmos

    public readonly Color[] GizmosColor = {Color.red, Color.yellow, Color.green, Color.blue, Color.magenta, Color.cyan, Color.gray, Color.black};   // 8 colors to draw neighbours

    public bool ShowSVO;
    public enum ShowSVOSelectionEnumerator
    {
        All, Layer, Selectable, Navegable
    }
    public ShowSVOSelectionEnumerator ShowSVOSelection;
    public int ShowSVOLayer;
    public bool[] ShowSVOToogleArray;
    public bool ShowSVOWithColor;
    public Color SVOCustomColor;

    public bool ShowNeighbours;
    public enum ShowNeighboursSelectionEnumerator       //He hecho 2 iguales por 2 motivos: 1º Se pueden meter cosas diferentes en el futuro en los 2. 2º Eso no va a pasar seguramente y soy retrasado.
    {
        All, Layer, Selectable
    }
    public ShowNeighboursSelectionEnumerator ShowNeighboursSelection;
    public bool ShowNeighboursWithColor;
    public bool[] ShowNeighboursToogleArray;
    public int ShowNeighboursLayer;
    public Color NeighboursCustomColor;

    public bool ShowLeaves;
    public enum LeavesRepresentationEnumerator
    {
        Opaque, Wire, Auto
    }
    public LeavesRepresentationEnumerator LeavesRepresentation;
    public enum LeavesShowSelectionEnumerator
    {
        All, Ocupied, Free
    }
    public LeavesShowSelectionEnumerator LeavesShowSelection;
    public Color LeavesColor;
    public float LeavesSize;

    public enum ColorRepresentationEnumerator
    {
        Custom, Auto
    }
    public ColorRepresentationEnumerator SVOColorRepresentation;
    public ColorRepresentationEnumerator NeighboursColorRepresentation;

    public bool ShowPathsDetailed;

    private void OnDrawGizmos()
    {
        if (SVO_DataStorage_SO == null)
        {
            print("SVO_DataStorage_SO not assigned.");
            return;
        }

        Gizmos.DrawWireCube(SVO_DataStorage_SO.position + Mathf.Pow(2, SVO_DataStorage_SO.rootSize) / 2 * Vector3.one, Mathf.Pow(2, SVO_DataStorage_SO.rootSize) * Vector3.one);

        if (CreatingSVO || SVO_DataStorage_SO.SVO == null)
        {
            return;
        }
        int lenght;

        if (ShowSVO)
        {
            if (ShowSVOSelection == ShowSVOSelectionEnumerator.All)
            {
                int numberNodes = 0;
                for (int i = 0; i < NumLayers; i++)
                {
                    lenght = SVO_DataStorage_SO.SVO[i] != null ? SVO_DataStorage_SO.SVO[i].Length : 0;
                    for (int j = 0; j < lenght; j++)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j] != null)
                        {
                            numberNodes++;
                            if (SVOColorRepresentation == ColorRepresentationEnumerator.Custom)
                            {
                                Gizmos.color = SVOCustomColor;
                            }
                            else if (SVOColorRepresentation == ColorRepresentationEnumerator.Auto)
                            {
                                Gizmos.color = GizmosColor[i%8];
                            }

                            Gizmos.DrawWireCube(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), 0.95f * SVO_ContructorHelper.GetVoxelSize(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers) * Vector3.one);
                        }
                    }
                }
            }
            else if (ShowSVOSelection == ShowSVOSelectionEnumerator.Layer)
            {
                lenght = SVO_DataStorage_SO.SVO[ShowSVOLayer] != null ? SVO_DataStorage_SO.SVO[ShowSVOLayer].Length : 0;
                for (int j = 0; j < lenght; j++)
                {
                    if (SVO_DataStorage_SO.SVO[ShowSVOLayer][j] != null)
                    {
                        if (SVOColorRepresentation == ColorRepresentationEnumerator.Custom)
                        {
                            Gizmos.color = SVOCustomColor;
                        }
                        else if (SVOColorRepresentation == ColorRepresentationEnumerator.Auto)
                        {
                            Gizmos.color = GizmosColor[ShowSVOLayer];
                        }

                        Gizmos.DrawWireCube(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[ShowSVOLayer][j], RootSize, NumLayers, ref SVO_GlobalPosition), 0.95f * SVO_ContructorHelper.GetVoxelSize(SVO_DataStorage_SO.SVO[ShowSVOLayer][j], RootSize, NumLayers) * Vector3.one);
                    }
                }
            }
            else if (ShowSVOSelection == ShowSVOSelectionEnumerator.Selectable)
            {
                for (int i = 1; i < NumLayers; i++)
                {
                    if (ShowSVOToogleArray[i] == false)
                    {
                        continue;
                    }

                    lenght = SVO_DataStorage_SO.SVO[i] != null ? SVO_DataStorage_SO.SVO[i].Length : 0;
                    for (int j = 0; j < lenght; j++)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j] != null)
                        {
                            if (SVOColorRepresentation == ColorRepresentationEnumerator.Custom)
                            {
                                Gizmos.color = SVOCustomColor;
                            }
                            else if (SVOColorRepresentation == ColorRepresentationEnumerator.Auto)
                            {
                                Gizmos.color = GizmosColor[i%8];
                            }

                            Gizmos.DrawWireCube(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), 0.95f * SVO_ContructorHelper.GetVoxelSize(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers) * Vector3.one);
                        }
                    }
                }
            }
            else if (ShowSVOSelection == ShowSVOSelectionEnumerator.Navegable)
            {
                //int numberNodes = 0;
                for (int i = 0; i < NumLayers; i++)
                {
                    lenght = SVO_DataStorage_SO.SVO[i] != null ? SVO_DataStorage_SO.SVO[i].Length : 0;
                    for (int j = 0; j < lenght; j++)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j].FirstChild != null || SVO_DataStorage_SO.SVO[i][j].TagZone == byte.MaxValue)
                        {
                            continue;
                        }
                       // numberNodes++;

                        if (SVOColorRepresentation == ColorRepresentationEnumerator.Custom)
                        {
                            Gizmos.color = SVOCustomColor;
                        }
                        else if (SVOColorRepresentation == ColorRepresentationEnumerator.Auto)
                        {
                            Gizmos.color = GizmosColor[i%8];
                        }

                        Gizmos.DrawWireCube(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), 0.95f * SVO_ContructorHelper.GetVoxelSize(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers) * Vector3.one);
                    }
                }
                //print("Number of Navegable Nodes: " + numberNodes);
            }
           
        }

        if (ShowLeaves)
        {
            for (int i = 0; i < NumLayers; i++)
            {
                lenght = SVO_DataStorage_SO.SVO[i] != null ? SVO_DataStorage_SO.SVO[i].Length : 0;
                for (int j = 0; j < lenght; j++)
                {
                    if (SVO_DataStorage_SO.SVO[i][j].FirstChild != null)
                    {
                        continue;
                    }

                    if (LeavesShowSelection == LeavesShowSelectionEnumerator.Ocupied)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j].TagZone != byte.MaxValue)
                        {
                            continue;
                        }
                    }
                    else if (LeavesShowSelection == LeavesShowSelectionEnumerator.Free)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j].TagZone == byte.MaxValue)
                        {
                            continue;
                        }
            }

            if (LeavesRepresentation == LeavesRepresentationEnumerator.Wire)
                    {
                        Gizmos.color = LeavesColor;
                    }
                    else if (LeavesRepresentation == LeavesRepresentationEnumerator.Auto)
                    {
                        Gizmos.color = GizmosColor[i % 8];
                    }
                    if (LeavesRepresentation == LeavesRepresentationEnumerator.Opaque)
                    {
                        Gizmos.DrawCube(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), LeavesSize * SVO_ContructorHelper.GetVoxelSize(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers) * Vector3.one);
                    }
                    else
                    {
                        Gizmos.DrawWireCube(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), LeavesSize * SVO_ContructorHelper.GetVoxelSize(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers) * Vector3.one);
                    }
                }
            }
        }

        if (ShowNeighbours)
        {
            if (ShowNeighboursSelection == ShowNeighboursSelectionEnumerator.All)
            {
                for (int i = 0; i < NumLayers - 1; i++)
                {
                    lenght = SVO_DataStorage_SO.SVO[i] != null ? SVO_DataStorage_SO.SVO[i].Length : 0;
                    for (int j = 0; j < lenght; j++)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j] != null && SVO_DataStorage_SO.SVO[i][j].Neighbours != null)
                        {
                            foreach (SVO_NodeData neighbour in SVO_DataStorage_SO.SVO[i][j].Neighbours)
                            {
                                if (NeighboursColorRepresentation == ColorRepresentationEnumerator.Custom)
                                {
                                    Gizmos.color = NeighboursCustomColor;
                                }
                                else if (NeighboursColorRepresentation == ColorRepresentationEnumerator.Auto)
                                {
                                    Gizmos.color = GizmosColor[i % 8];
                                }
                                Gizmos.DrawLine(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), SVO_ContructorHelper.GetNodeGlobalPosition(neighbour, RootSize, NumLayers, ref SVO_GlobalPosition));
                            }
                        }
                    }
                }
            }
            else if (ShowNeighboursSelection == ShowNeighboursSelectionEnumerator.Layer)
            {
                lenght = SVO_DataStorage_SO.SVO[ShowNeighboursLayer] != null ? SVO_DataStorage_SO.SVO[ShowNeighboursLayer].Length : 0;
                for (int j = 0; j < lenght; j++)
                {
                    if (SVO_DataStorage_SO.SVO[ShowNeighboursLayer][j] != null && SVO_DataStorage_SO.SVO[ShowNeighboursLayer][j].Neighbours != null)
                    {
                        foreach (SVO_NodeData neighbour in SVO_DataStorage_SO.SVO[ShowNeighboursLayer][j].Neighbours)
                        {
                            if (NeighboursColorRepresentation == ColorRepresentationEnumerator.Custom)
                            {
                                Gizmos.color = NeighboursCustomColor;
                            }
                            else if (NeighboursColorRepresentation == ColorRepresentationEnumerator.Auto)
                            {
                                Gizmos.color = GizmosColor[ShowNeighboursLayer];
                            }

                            Gizmos.DrawLine(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[ShowNeighboursLayer][j], RootSize, NumLayers, ref SVO_GlobalPosition), SVO_ContructorHelper.GetNodeGlobalPosition(neighbour, RootSize, NumLayers, ref SVO_GlobalPosition));
                        }
                    }
                }
            }
            else if (ShowNeighboursSelection == ShowNeighboursSelectionEnumerator.Selectable)
            {
                for (int i = 0; i < NumLayers - 1; i++)
                {
                    if (ShowNeighboursToogleArray[i] == false)
                    {
                        continue;
                    }

                    lenght = SVO_DataStorage_SO.SVO[i] != null ? SVO_DataStorage_SO.SVO[i].Length : 0;
                    for (int j = 0; j < lenght; j++)
                    {
                        if (SVO_DataStorage_SO.SVO[i][j] != null && SVO_DataStorage_SO.SVO[i][j].Neighbours != null)
                        {
                            foreach (SVO_NodeData neighbour in SVO_DataStorage_SO.SVO[i][j].Neighbours)
                            {
                                if (NeighboursColorRepresentation == ColorRepresentationEnumerator.Custom)
                                {
                                    Gizmos.color = NeighboursCustomColor;
                                }
                                else if (NeighboursColorRepresentation == ColorRepresentationEnumerator.Auto)
                                {
                                    Gizmos.color = GizmosColor[i % 8];
                                }

                                Gizmos.DrawLine(SVO_ContructorHelper.GetNodeGlobalPosition(SVO_DataStorage_SO.SVO[i][j], RootSize, NumLayers, ref SVO_GlobalPosition), SVO_ContructorHelper.GetNodeGlobalPosition(neighbour, RootSize, NumLayers, ref SVO_GlobalPosition));
                            }
                        }
                    }
                }
            }
        }
    }

    #endregion
}

