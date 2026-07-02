using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


[CreateAssetMenu(fileName = "SVO_DataStorage", menuName = "Scriptable Objects/SVO_DataStorage")]
public class SVO_DataStorage : ScriptableObject, ISerializationCallbackReceiver
{
    [Tooltip("Tamaño en metros cúbicos del SVO = 2 ^ rootSize. Así siempre son potencias de 2 los voxeles.")]
    public int rootSize;        

    [Tooltip("Número de capas/divisiones que tiene el octree. Hay que tener en cuenta los tamaños de los bichos para la navegación.")]
    [HideInInspector] public int numLayers { get => rootSize - leafSize + 1; }      //Número de capas que va a tener el octree. Cuantas más capas, mejor resolución y más almacenaje requerido. Tiene que ir de acorde al diseño del tamaño de los bichos.

    [Tooltip("Tamaño en metros cúbicos de los voxeles más pequeños del SVO = 2 ^ leafSize")]
    public int leafSize;

    [Tooltip("Posición del SVO")]
    public Vector3 position;           

    [Tooltip("Capa física en la que consideramos que un objeto con collider es un obstáculo para el SVO.")]
    public LayerMask obstacleLayer;     //Capa física en la que consideramos que un objeto con collider es un obstáculo para el SVO.

    //public struct SVO_Layer
    //{
    //    public SVO_VoxelData[] SVO_LayerData;             //Posible solución. Pd: Ya no. Me niego.
    //}

    //public SVO_Layer[] SVO_Data;

    [Tooltip("Variable que contiene toda la info de la Nav Mesh en 3D. La primera matriz representa las capas del SVO y dentro de cada capa se almacenan todos los voxeles de esa capa. \n Es un array (número de la capa del SVO) de arrays (voxeles dentro de esa capa).")]
    public SVO_NodeData[][] SVO;       /*  Variable que contiene toda la info de la Nav Mesh en 3D. La primera matriz representa las capas del SVO y dentro de cada capa se almacenan todos los voxeles de esa capa. 
                                         *  Es un array (número de la capa del SVO) de arrays (voxeles dentro de esa capa). */

    //[Serializable]
    //public struct SVOPack
    //{
    //    public SVO_VoxelData[][] SVO;
    //    public int size;
    //    public int pos;
    //}

    [Serializable]
    public struct Zones
    {
        public byte layer;
        public Bounds bounds;
    }

    [Tooltip("Se utiliza para marcar zonas en los voxels NAVEGABLES. \n 0: Default.\n 1: Por ahora no asignado. \n ... \n 255: Caso especial. NO ASIGNAR.")]
    public Zones[] zoneList;

    /*  Layers 0 - 255  Se utiliza para marcar zonas en los voxels NAVEGABLES.
     *  0: Default.
     *  1: Por ahora no asignado. 
     *  
     * ...
     * 
     * 255: Caso especial. Se usa para indicar colisión en los nodos/voxels hoja.
     */


    public delegate void CreateSVODelegate();
    public CreateSVODelegate CreateSVOHandler;


    [ContextMenu("SVO Simplified Info")]
    private void SVOSimplifiedInfo()
    {
        if (SVO == null)
        {
            Debug.Log("SVO: null");
            return;
        }

        for (int i = 0; i < numLayers; i++)
        {
            Debug.Log("Layer: " + i);
            Debug.Log("Length: " + SVO[i].Length);
        }
    }

    [ContextMenu("SVO Info")]
    private void SVOFullInfo()
    {
        if (SVO == null)
        {
            Debug.Log("SVO: null");
            return;
        }

        for (int i = 0; i < numLayers; i++)
        {
            Debug.Log("Layer: " + i);
            Debug.Log("Length: " + SVO[i].Length);
            for (int j = 0; j < SVO[i].Length; j++)
            {
                Debug.Log("Node: " + SVO[i][j].Link);
            }
        }
    }

    [ContextMenu("SVO Pruebas")]
    private void SVOPruebas()
    {
        if (SVO == null)
        {
            Debug.Log("SVO: null");
            return;
        }

        for (int i = 0; i < numLayers; i++)
        {
            for (int j = 0; j < SVO[i].Length; j++)
            {
                if (SVO[i][j].FirstChild != null && (SVO[i][j].FirstChild.Link & 7) != 0)
                {
                    //a = Convert.ToString(SVO[i][j].FirstChild.Link, 2);
                    //Debug.Log("Binary: " + a);
                    Debug.Log("nOT HEHE");
                }
                else
                {
                    Debug.Log("Todo correcto");
                }
            }
        }
    }


    private void OnEnable()
    {
        Debug.Log("On Enable");
    }

    private void Awake()
    {
        Debug.Log("Awake");
    }

    [SerializeField, HideInInspector] private List<SVO_NodeData> serializeAux = new();
    [SerializeField, HideInInspector] private int[] layersLenght; 
    public void OnBeforeSerialize()
    {
        Debug.Log("On Before Serialization");

#if UNITY_EDITOR
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsDirty(this))
        {
            return;
        }
#endif
        Debug.Log("Doing On Before Serialization");
        if (SVO != null)
        {
            serializeAux.Clear();
            layersLenght = new int[numLayers];

            for (int i = 0; i < numLayers; i++)
            {
                layersLenght[i] = SVO[i].Length;

                for (int j = 0; j < SVO[i].Length; j++)
                {
                    serializeAux.Add(SVO[i][j]);
                }
            }
        }
    }

    public void OnAfterDeserialize()
    {
        Debug.Log("On After Serialization");

        if (serializeAux.Count != 0)
        {
            int count = 0;
            SVO = new SVO_NodeData[numLayers][];

            for (int i = 0; i < numLayers; i++)
            {   
                SVO[i] = new SVO_NodeData[layersLenght[i]];

                serializeAux.CopyTo(count, SVO[i], 0, layersLenght[i]);
                count += layersLenght[i];
            }

            serializeAux.Clear();
            layersLenght = null;    
        }
    }
}