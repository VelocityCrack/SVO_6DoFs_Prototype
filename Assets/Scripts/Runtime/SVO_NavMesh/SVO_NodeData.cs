using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;

[Serializable]
public class SVO_NodeData
{
    [SerializeReference]
    public SVO_NodeData FirstChild;

    [SerializeReference]
    public SVO_NodeData Parent;

    [SerializeReference, SerializeField]
    public List<SVO_NodeData> Neighbours;      //Seis vecinos

    public byte TagZone;        /*Zona especial en la que está el voxel. P. ej: Cueva, zona de alta presión, etc. Se utiliza una máscara, por lo que pueden haber hasta 256 zonas de tipo diferente. 
                                 * La defualt es la 0. El 11111111 (255) está reservado para los nodos hoja, que no hay manera si no de saber si están detectando obstáculo. */
                                    

    public uint Link;       /* De izquiera a derecha en la máscara
                             * Level: 4 bits (Capa a la que pertenece. Tiene un máximo de 16 capas: De 0000 a 1111. En este proyecto vamos a utilizar 4: 0000, 0001, 0010 y 0011.)
                             * Index: 28 bits (Hasta 268.435.456 voxeles) */
}

public class SVO_NodeDataComparer : IComparable
{
    public static IComparer SortNodeNode()
    {
        return (IComparer) new SortNodeNodeHelper();
    }

    private class SortNodeNodeHelper : IComparer
    {
        public int Compare(object x, object y)
        {
            SVO_NodeData a = (SVO_NodeData)x;
            SVO_NodeData b = (SVO_NodeData)y;

            return a.Link >= b.Link ? 1 : -1;
        }
    }

    public static IComparer SortNodeLink()
    {
        return (IComparer) new SortNodeLinkHelper();
    }

    private class SortNodeLinkHelper : IComparer
    {
        public int Compare(object x, object y)
        {
            SVO_NodeData a = (SVO_NodeData)x;
            uint b = (uint)y;

            if (a.Link > b)
            {
                return 1;
            } 
            else if (a.Link < b)
            {
                return -1;
            }
            else
            {
                return 0;
            }
        }
    }

    public int CompareTo(object obj)
    {
        throw new NotImplementedException();
    }
}

//public class SVO_VoxelDataComparer2 : IComparer<SVO_VoxelData>
//{
//    public int Compare(SVO_VoxelData x, uint y)
//    {
//        return x.Link >= y ? 1 : -1;
//    }

//    public int Compare(SVO_VoxelData x, SVO_VoxelData y)
//    {
//        throw new NotImplementedException();
//    }
//}
