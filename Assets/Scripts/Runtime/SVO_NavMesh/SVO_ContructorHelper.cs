using System;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI;

public class SVO_ContructorHelper
{

    #region Auxiliuary functions

    public static bool CheckForCollision(SVO_NodeData node, LayerMask obstacleLayer, int rootSize, int numLayers, ref Vector3 SVO_globalPosition)
    {
        //BoxVisualizer.DisplayBox(GetRealNodePosition(node), Vector3.one * GetVoxelSize(node) / 2, Quaternion.identity);
        return Physics.CheckBox(GetNodeGlobalPosition(node, rootSize, numLayers, ref SVO_globalPosition), Vector3.one * GetVoxelSize(node, rootSize, numLayers) / 2, Quaternion.identity, obstacleLayer);
    }

    public static byte CheckTagZone(SVO_NodeData node, ref SVO_DataStorage.Zones[] zonesList, int rootSize, int numLayers, ref Vector3 SVO_globalPosition)
    {
        //Se comprueba si el voxel está dentro de una tag zone. Para ello se llama a GetOctreeNodeLimits para saber lo que acupa el voxel.
        Vector3[] limits = GetOctreeNodeLimits(node, rootSize, numLayers, ref SVO_globalPosition);
        Bounds bounds = new()
        {
            center = limits[0] + ((limits[1] - limits[0]) / 2),
            size = limits[1] - limits[0]
        };

        for (int i = 0; i < zonesList.Length; i++)
        {
            if (bounds.Contains(zonesList[i].bounds.center))
            {
                return zonesList[i].layer;
            }
        }

        return 0;
    }

    public static uint GetFirstChildLinkFromParent(SVO_NodeData parentNode)        //Devuelve el link del primer hijo a partir del link del padre
    {
        uint childLayer = GetLayer(parentNode) - 1;
        Vector3 localPos = GetNodeLocalPosition(parentNode);

        uint newPos = MortonCode.EncodeMorton3((uint)localPos.x * 2, (uint)localPos.y * 2, (uint)localPos.z * 2);

        return newPos + (childLayer << 28);
    }

    public static uint GetLayer(SVO_NodeData node)       //Devuelve la capa del octree en el que se haya el nodo a raíz del link. Min: 0  Max: Número de capas - 1
    {
        return ((uint.MaxValue - ((1 << 28) - 1)) & node.Link) >> 28;
    }

    public static uint GetLayer(uint nodeLink)       //Devuelve la capa del octree en el que se haya el nodo a raíz del link. Min: 0  Max: Número de capas - 1
    {
        return ((uint.MaxValue - ((1 << 28) - 1)) & nodeLink) >> 28;
    }

    public static uint GetMortonPosition(SVO_NodeData node)      //Devuelve la posición en Morton Code a raíz del link.
    {
        return node.Link & ((1 << 28) - 1);
    }

    public static float GetVoxelSize(SVO_NodeData node, int rootSize, int numLayers)
    {
        return rootSize / Mathf.Pow(2, numLayers - GetLayer(node) - 1);
    }

    #endregion

    #region Voxel link to global or local position
    public static Vector3 GetNodeGlobalPosition(SVO_NodeData node, int rootSize, int numLayers, ref Vector3 SVO_globalPosition)      //Returns the center of the voxel in world coordinates
    {
        float voxelSize = GetVoxelSize(node, rootSize, numLayers);
        Vector3 localPos = GetNodeLocalPosition(node);

        return new(SVO_globalPosition.x + (localPos.x * voxelSize + (voxelSize / 2)), SVO_globalPosition.y + (localPos.y * voxelSize + (voxelSize / 2)), SVO_globalPosition.z + (localPos.z * voxelSize + (voxelSize / 2)));
    }

    public static Vector3[] GetOctreeNodeLimits(SVO_NodeData node, int rootSize, int numLayers, ref Vector3 position)      //No sé si hará falta. Supongo que no. Pero ahí está. PD: Sí hace falta.
    {
        float voxelSize = GetVoxelSize(node, rootSize, numLayers);
        Vector3 localPos = GetNodeLocalPosition(node);

        Vector3 minPos = new(position.x + (localPos.x * voxelSize), position.y + (localPos.y * voxelSize), position.z + (localPos.z * voxelSize));
        Vector3 maxPos = new(position.x + (localPos.x * voxelSize + voxelSize), position.y + (localPos.y * voxelSize + voxelSize), position.z + (localPos.z * voxelSize + voxelSize));

        return new Vector3[] { minPos, maxPos };
    }

    public static Vector3 GetNodeLocalPosition(SVO_NodeData node)      //Devuelve la coordenada local en el octree del voxel.
    {
        uint mortonPos = GetMortonPosition(node);

        return new(MortonCode.DecodeMorton3X(mortonPos), MortonCode.DecodeMorton3Y(mortonPos), MortonCode.DecodeMorton3Z(mortonPos));
    }

    #endregion


    //private SVO_VoxelData GetNodeInSVO(Vector3 position, ref SVO_VoxelData[][] SVO, int numLayers)
    //{
    //    SVO_VoxelData node = SVO[numLayers - 1][0];


    //    if (SearchTree(node) != node)
    //    {

    //    }
    //    else
    //    {
    //        return node;
    //    }


    //    return null;
    //}

    #region NodeSearching

    /// <summary>
    /// Busca un nodo en el SVO a partir de una posición global.
    /// </summary>
    /// <param name="targetGlobalPos">La posición global del nodo que se desea buscar.</param>
    /// <param name="SVO">Referencia del SVO (Sparse Voxel Octree) donde se realizará la búsqueda.</param>
    /// <param name="rootSize">El tamaño en el espacio del lado del cuadraro que forma la raíz del SVO.</param>
    /// <param name="SVO_globalPosition">La posición global del SVO.</param>
    /// <returns>El nodo más pequeño encontrado en el SVO que abarque esa posición o null si la posición no está dentro del SVO.</returns>
    public static SVO_NodeData NodeSearch(Vector3 targetGlobalPos, ref SVO_NodeData[][] SVO, int rootSize, ref Vector3 SVO_globalPosition)        
    {
        Bounds bounds = new()
        {
            center = SVO_globalPosition + rootSize / 2 * Vector3.one,
            extents = rootSize / 2 * Vector3.one,
        };

        int numLayers = SVO.Length;
        SVO_NodeData targetNode = SVO[numLayers - 1][0];
        int depth = 1;

        if (!bounds.Contains(targetGlobalPos))        //Comprueba que la posición está dentro del SVO
        {
            return null;
        }

        while (targetNode.FirstChild != null)       //Hace una búsqueda en árbol hasta que encuentra un nodo sin hijos que abarque la posición. Ese es el resultado.
        {
            depth++;

            targetNode = targetNode.FirstChild;

            int arrayPos = Array.BinarySearch(SVO[numLayers - depth], targetNode.Link, SVO_NodeDataComparer.SortNodeLink());

            if (arrayPos >= 0)
            {
                bounds.extents /= 2;
                for (int i = 0; i < 8; i++)
                {
                    bounds.center = GetNodeGlobalPosition(SVO[numLayers - depth][arrayPos + i], rootSize, numLayers, ref SVO_globalPosition);

                    if (bounds.Contains(targetGlobalPos))
                    {
                        targetNode = SVO[numLayers - depth][arrayPos + i];
                        break;
                    }
                }
            }
            else
            {
                return null;
            }
        }

        return targetNode;
    }

    /// <inheritdoc cref="NodeSearch(Vector3, ref SVO_NodeData[][], int, ref Vector3)" path="/summary"/>
    /// <param name="targetGlobalPos"> La posición global del nodo que se desea buscar.</param>
    /// <param name="SVO"> Referencia del SVO (Sparse Voxel Octree) donde se realizará la búsqueda.</param>
    /// <param name="rootSize"> El tamaño en el espacio del lado del cuadraro que forma la raíz del SVO.</param>
    /// <param name="SVO_globalPosition"> La posición global del SVO.</param>
    /// <param name="layer"> La capa hasta la cual se desea buscar el nodo.</param>
    /// <returns> El nodo más pequeño hasta la capa especificada por <paramref name="layer"/> encontrado en el SVO que abarque esa posición o null si la posición no está dentro del SVO.</returns>
    public static SVO_NodeData NodeSearch(Vector3 targetGlobalPos, ref SVO_NodeData[][] SVO, int rootSize, ref Vector3 SVO_globalPosition, int layer)
    {
        Bounds bounds = new()
        {
            center = SVO_globalPosition + rootSize / 2 * Vector3.one,
            extents = rootSize / 2 * Vector3.one,
        };

        int numLayers = SVO.Length;
        SVO_NodeData targetNode = SVO[numLayers - 1][0];
        int depth = 1;

        if (!bounds.Contains(targetGlobalPos))
        {
            return null;
        }

        while (targetNode.FirstChild != null && depth + layer < numLayers)
        {
            depth++;

            targetNode = targetNode.FirstChild;

            int arrayPos = Array.BinarySearch(SVO[numLayers - depth], targetNode.Link, SVO_NodeDataComparer.SortNodeLink());

            if (arrayPos >= 0)
            {
                bounds.extents /= 2;
                for (int i = 0; i < 8; i++)
                {
                    bounds.center = GetNodeGlobalPosition(SVO[numLayers - depth][arrayPos + i], rootSize, numLayers, ref SVO_globalPosition);

                    if (bounds.Contains(targetGlobalPos))
                    {
                        targetNode = SVO[numLayers - depth][arrayPos + i];
                        break;
                    }
                }
            }
            else
            {
                throw new Exception("Search Tree ha fallado");
            }
        }

        if (targetNode.FirstChild != null)
        {
            return null;
        }
        return targetNode;
    }
    #endregion
}
