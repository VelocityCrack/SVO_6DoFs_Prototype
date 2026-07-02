using UnityEngine;
using UnityEditor;
using Unity.VisualScripting;
using System.Collections;
using static SVO_Constructor;
using System.Diagnostics.Eventing.Reader;

namespace PRO3.Editor
{
    [CustomEditor(typeof(SVO_Constructor))]
    public class SVO_ConstructorEditor : UnityEditor.Editor
    {
        SVO_Constructor m_SVO_Constructor;
        SerializedProperty m_RootSize;
        SerializedProperty m_NumLayers;

        SerializedProperty m_SVO_DataStorage_SO;
        SerializedProperty m_CreatingSVO;

        SerializedProperty m_ShowSVO;
        SerializedProperty m_ShowSVOWithColor;
        SerializedProperty m_ShowSVOSelection;
        SerializedProperty m_ShowSVOLayer;
        SerializedProperty m_SVOColorRepresentation;
        SerializedProperty m_SVOCustomColor;
        

        SerializedProperty m_ShowNeighbours;
        SerializedProperty m_ShowNeighboursWithColor;
        SerializedProperty m_ShowNeighboursLayer;
        SerializedProperty m_ShowNeighboursSelection;
        SerializedProperty m_NeighboursColorRepresentation;
        SerializedProperty m_NeighboursCustomColor;

        SerializedProperty m_ShowLeaves;
        SerializedProperty m_LeavesRepresentation;
        SerializedProperty m_LeavesShowSelection;
        SerializedProperty m_LeavesColor;
        SerializedProperty m_LeavesSize;

        SerializedProperty m_ShowPathsDetailed;

        bool m_HasOrderedCreatingSVO;
        bool m_ShowSVOSelectableToogle;
        bool m_ShowNeighboursSelectableToogle;
        

        private void OnEnable()
        {
            m_RootSize = serializedObject.FindProperty("RootSize");
            m_NumLayers = serializedObject.FindProperty("NumLayers");
            m_SVO_DataStorage_SO = serializedObject.FindProperty("SVO_DataStorage_SO");
            m_CreatingSVO = serializedObject.FindProperty("CreatingSVO");
            m_ShowSVO = serializedObject.FindProperty("ShowSVO");
            m_ShowSVOWithColor = serializedObject.FindProperty("ShowSVOWithColor");
            m_ShowSVOSelection = serializedObject.FindProperty("ShowSVOSelection");
            m_ShowSVOLayer = serializedObject.FindProperty("ShowSVOLayer");
            m_SVOColorRepresentation = serializedObject.FindProperty("SVOColorRepresentation");
            m_SVOCustomColor = serializedObject.FindProperty("SVOCustomColor"); m_SVOCustomColor.colorValue = Color.white;
            //m_ShowSVOToogleArray = serializedObject.FindProperty("ShowSVOSelectable");

            m_ShowNeighbours = serializedObject.FindProperty("ShowNeighbours");
            m_ShowNeighboursWithColor = serializedObject.FindProperty("ShowNeighboursWithColor");
            m_ShowNeighboursLayer = serializedObject.FindProperty("ShowNeighboursLayer");
            m_ShowNeighboursSelection = serializedObject.FindProperty("ShowNeighboursSelection");
            m_NeighboursColorRepresentation = serializedObject.FindProperty("NeighboursColorRepresentation");
            m_NeighboursCustomColor = serializedObject.FindProperty("NeighboursCustomColor"); m_NeighboursCustomColor.colorValue = Color.white; 

            m_ShowLeaves = serializedObject.FindProperty("ShowLeaves");
            m_LeavesColor = serializedObject.FindProperty("LeavesColor");  m_LeavesColor.colorValue = Color.red;
            m_LeavesSize = serializedObject.FindProperty("LeavesSize");
            m_LeavesRepresentation = serializedObject.FindProperty("LeavesRepresentation");
            m_LeavesShowSelection = serializedObject.FindProperty("LeavesShowSelection");

            m_ShowPathsDetailed = serializedObject.FindProperty("ShowPathsDetailed");
        }

        public override void OnInspectorGUI()
        {
            m_SVO_Constructor = (SVO_Constructor)target;

            serializedObject.Update();

            EditorGUILayout.LabelField(m_SVO_Constructor.name.ToUpper(), EditorStyles.boldLabel);
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Root Size: " + m_RootSize.intValue + " m3");
            EditorGUILayout.LabelField("Number of Layers: " + m_NumLayers.intValue);
            EditorGUILayout.LabelField("Leaf Size: " + (float)m_RootSize.intValue / Mathf.Pow(2, m_NumLayers.intValue - 1) + " m3");

            EditorGUILayout.Space(2);

            EditorGUILayout.LabelField("Creating SVO: " + m_CreatingSVO.boolValue);

            EditorGUILayout.Space(15);

            //SETTINGS
            EditorGUILayout.LabelField("Settings".ToUpper(), EditorStyles.boldLabel);

            EditorGUILayout.Space(5);

            //Show SVO
            EditorGUILayout.PropertyField(m_ShowSVO);
            if (m_ShowSVO.boolValue)
            {
                EditorGUILayout.PropertyField(m_ShowSVOSelection);
                if (m_ShowSVOSelection.enumValueIndex == (int)ShowSVOSelectionEnumerator.Layer)
                {
                    EditorGUILayout.Space(5);
                    m_ShowSVOLayer.intValue = EditorGUILayout.IntSlider("Number of Layers to Show: ", m_ShowSVOLayer.intValue, 0, m_NumLayers.intValue - 1);
                    
                } else if (m_ShowSVOSelection.enumValueIndex == (int)ShowSVOSelectionEnumerator.Selectable)
                {
                    EditorGUILayout.Space(5);

                    if (!m_ShowSVOSelectableToogle)
                    {
                        for (int i = 1; i < m_NumLayers.intValue; ++i)
                        {
                            m_SVO_Constructor.ShowSVOToogleArray[i] = false;
                        }
                    }

                    m_ShowSVOSelectableToogle = EditorGUILayout.BeginToggleGroup("Select Layers: ", m_ShowSVOSelectableToogle);
                    for (int i = 1; i < m_NumLayers.intValue; i++)
                    {
                        m_SVO_Constructor.ShowSVOToogleArray[i] = EditorGUILayout.Toggle("Layer " + i + ": ", m_SVO_Constructor.ShowSVOToogleArray[i]);
                    }
                    EditorGUILayout.EndToggleGroup();
                }
                
                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(m_SVOColorRepresentation);
                if (m_SVOColorRepresentation.intValue == (int)ColorRepresentationEnumerator.Custom)
                {
                    EditorGUILayout.PropertyField(m_SVOCustomColor);
                }
            }
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("--------------------------------------------");
            EditorGUILayout.Space(5);

            //Show Neighbours
            EditorGUILayout.PropertyField(m_ShowNeighbours);
            if (m_ShowNeighbours.boolValue)
            {
                EditorGUILayout.PropertyField(m_ShowNeighboursSelection);
                if (m_ShowNeighboursSelection.enumValueIndex == (int)ShowNeighboursSelectionEnumerator.Layer)
                {
                    EditorGUILayout.Space(5);
                    m_ShowNeighboursLayer.intValue = EditorGUILayout.IntSlider("Number of Layers to Show: ", m_ShowNeighboursLayer.intValue, 0, m_NumLayers.intValue - 2);

                }
                else if (m_ShowNeighboursSelection.enumValueIndex == (int)ShowNeighboursSelectionEnumerator.Selectable)
                {
                    EditorGUILayout.Space(5);

                    if (!m_ShowNeighboursSelectableToogle)
                    {
                        for (int i = 1; i < m_NumLayers.intValue - 1; ++i)
                        {
                            m_SVO_Constructor.ShowNeighboursToogleArray[i] = false;
                        }
                    }

                    m_ShowNeighboursSelectableToogle = EditorGUILayout.BeginToggleGroup("Select Layers: ", m_ShowNeighboursSelectableToogle);
                    for (int i = 1; i < m_NumLayers.intValue - 1; i++)
                    {
                        m_SVO_Constructor.ShowNeighboursToogleArray[i] = EditorGUILayout.Toggle("Layer " + i + ": ", m_SVO_Constructor.ShowNeighboursToogleArray[i]);
                    }
                    EditorGUILayout.EndToggleGroup();
                }

                EditorGUILayout.Space(5);
                EditorGUILayout.PropertyField(m_NeighboursColorRepresentation);
                if (m_NeighboursColorRepresentation.intValue == (int)ColorRepresentationEnumerator.Custom)
                {
                    EditorGUILayout.PropertyField(m_NeighboursCustomColor);
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("--------------------------------------------");
            EditorGUILayout.Space(5);

            //Show Leaves
            EditorGUILayout.PropertyField(m_ShowLeaves);
            if (m_ShowLeaves.boolValue)
            {
                EditorGUILayout.PropertyField(m_LeavesShowSelection);
                EditorGUILayout.PropertyField(m_LeavesRepresentation);
                if (m_LeavesRepresentation.intValue != (int)LeavesRepresentationEnumerator.Auto)
                {
                    EditorGUILayout.PropertyField(m_LeavesColor);
                }
                m_LeavesSize.floatValue = EditorGUILayout.Slider("Leaves Size: ", m_LeavesSize.floatValue, 0, 1);
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("--------------------------------------------");
            EditorGUILayout.Space(5);

            EditorGUILayout.PropertyField(m_ShowPathsDetailed);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("--------------------------------------------");
            EditorGUILayout.Space(15);

            EditorGUILayout.PropertyField(m_SVO_DataStorage_SO);

            //EditorGUILayout.PropertyField(m_RootSize);

            EditorGUILayout.Space(20);

            EditorGUILayout.LabelField("Generators".ToUpper(), EditorStyles.boldLabel);

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Initialize SVO") && !m_SVO_Constructor.CreatingSVO)
            {
                m_HasOrderedCreatingSVO = true;
                m_SVO_Constructor.InitializeSVO();
            }

            if (m_HasOrderedCreatingSVO && !m_SVO_Constructor.CreatingSVO)
            {
                m_HasOrderedCreatingSVO = false;
                SaveData();
            }

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Clean SVO") && !m_SVO_Constructor.CreatingSVO)
            {
                m_SVO_Constructor.SVO_DataStorage_SO.SVO = null;
                SaveData();
            }

            EditorGUILayout.Space(10);

            if (GUILayout.Button("Reset SVO Creation \n (Warning, do not use if the SVO is generating)"))
            {
                m_SVO_Constructor.CreatingSVO = false;
            }

            serializedObject.ApplyModifiedProperties();
        }

        void SaveData()
        {
            EditorUtility.SetDirty(m_SVO_Constructor.SVO_DataStorage_SO);
            // EditorUtility.CopySerialized
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}