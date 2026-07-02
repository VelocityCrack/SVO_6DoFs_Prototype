using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class IA_Director : MonoBehaviour
{
    public static IA_Director Instance { get; private set; } = null;
    private void Awake()
    {
        print("IA_Director Awake");
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        SVO_Constructor_GO = FindAnyObjectByType<SVO_Constructor>().gameObject;
    }

    [HideInInspector] public GameObject SVO_Constructor_GO;
}
