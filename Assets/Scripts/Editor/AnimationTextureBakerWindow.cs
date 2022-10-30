using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[ExecuteAlways]
public class AnimationTextureBakerWindow : EditorWindow
{
    public static AnimationTextureBakerWindow WindowInstance;

    bool m_Initialized = false;

    private const string CURR_SELECT_LABEL = "Selected FBX:";
    GameObject m_SelectedObject;
    SkinnedMeshRenderer m_SelectedSkinnedMesh;

    //Display and pickable animations
    List<Object> m_Animations = new List<Object>();
    List<string> m_AnimationsNames = new List<string>();
    AnimationClip m_SelectedAnimation = null;
    float m_SelectedAnimationCompression = 0;
    int m_SelectedAnimationIndex = 0;

    //Animations that will be baked
    List<AnimationClip> m_PickedAnimations = new List<AnimationClip>();
    List<float> m_PickedAnimationsCompressions = new List<float>();

    string m_Path = "";
    string m_FolderPath = "";

    private const string COMPUTE_SHADER_PATH = "Editor/Shaders/{0}";
    private const string COMPUTE_SHADER_NAME = "AnimationTextureBaker";
    private const string INTERNAL_COMPUTE_TEXTURE_NAME = "_Out";
    ComputeShader m_ComputeBaker = null;

    int m_SelectedBakeOptionIndex = 0;
    string[] m_BakeOptionsNames = new string[] { "GPU", "CPU" };

    int m_SelectedBakeTypeIndex = 0;
    string[] m_BakeTypeNames = new string[] { "Bake Distance", "Bake Positions" };

    GraphicsFormat m_RenderTextureFormat = GraphicsFormat.R16G16B16A16_SFloat;
    Texture2D m_TextureData;

    bool m_UseInternalNamingConvention = true;
    private const string ASSET_NAME_CONVENTION = "{0}_{1}_VAT.asset";
    string m_FileName = "";


    private void OnGUI()
    {
        if (!m_Initialized)
        {
            Init();
        }

        using (new EditorGUILayout.VerticalScope())
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(CURR_SELECT_LABEL);
                using (new EditorGUILayout.HorizontalScope())
                {
                    //GUIContent lockTex = m_LockSelected ? EditorGUIUtility.IconContent("LockIcon-On") : EditorGUIUtility.IconContent("LockIcon");

                    string objName = m_SelectedObject == null ? "N/A" : m_SelectedObject.name;
                    EditorGUI.BeginChangeCheck();
                    m_SelectedObject = (GameObject)EditorGUILayout.ObjectField(m_SelectedObject, typeof(GameObject), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        ChangeSelected();
                    }
                }


                if (m_SelectedObject != null)
                {
                    //Draw picked obj animations
                    if (m_AnimationsNames.Count > 0)
                    {
                        GUILayout.Space(5);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label("Animations:");
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Add all"))
                            {
                                for (int i = 0; i < m_Animations.Count; i++)
                                {
                                    m_PickedAnimations.Add(m_Animations[i] as AnimationClip);
                                    m_PickedAnimationsCompressions.Add(m_SelectedAnimationCompression);
                                }
                                UpdateFileName();
                            }
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUI.BeginChangeCheck();
                            m_SelectedAnimationIndex = EditorGUILayout.Popup(m_SelectedAnimationIndex, m_AnimationsNames.ToArray());
                            if (EditorGUI.EndChangeCheck() && m_Animations.Count > 0)
                            {
                                m_SelectedAnimation = m_Animations[m_SelectedAnimationIndex] as AnimationClip;
                            }
                            if (GUILayout.Button("+"))
                            {
                                m_PickedAnimations.Add(m_SelectedAnimation);
                                m_PickedAnimationsCompressions.Add(m_SelectedAnimationCompression);
                                UpdateFileName();
                            }
                        }
                    }

                    m_SelectedAnimationCompression = EditorGUILayout.Slider("Clip compression: ", m_SelectedAnimationCompression, 0, 1);
                }
            }
            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(string.Format("Picked Animations: {0}", m_PickedAnimations.Count.ToString()));
                    if (m_PickedAnimations.Count > 0)
                    {
                        if (GUILayout.Button("Clear"))
                        {
                            m_PickedAnimations.Clear();
                            m_PickedAnimationsCompressions.Clear();
                            UpdateFileName();
                        }
                    }
                }
                for (int i = 0; i < m_PickedAnimations.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = false;
                        EditorGUILayout.ObjectField(m_PickedAnimations[i], typeof(AnimationClip), false);
                        GUI.enabled = true;
                        m_PickedAnimationsCompressions[i] = EditorGUILayout.Slider(m_PickedAnimationsCompressions[i], 0, 1, GUILayout.MaxWidth(120));
                        if (GUILayout.Button("-"))
                        {
                            m_PickedAnimations.RemoveAt(i);
                            m_PickedAnimationsCompressions.RemoveAt(i);
                            UpdateFileName();
                        }
                    }
                }
            }

            EditorGUILayout.Space(10);

            if (m_SelectedAnimation != null && m_SelectedSkinnedMesh != null && m_SelectedObject != null)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("Output info: ");
                    int framesCount = 0;
                    for (int i = 0; i < m_PickedAnimations.Count; i++)
                    {
                        framesCount += (int)(m_PickedAnimations[i].length * m_PickedAnimations[i].frameRate * (1 - m_PickedAnimationsCompressions[i]));
                    }
                    string expectedSize = string.Format("Expected size: {0}x{1}", m_SelectedSkinnedMesh.sharedMesh.vertexCount, framesCount);
                    EditorGUILayout.LabelField(expectedSize);
                    EditorGUILayout.LabelField("Vertices: " + m_SelectedSkinnedMesh.sharedMesh.vertexCount);
                    EditorGUILayout.LabelField("Total Frames: " + framesCount);
                }
            }

            GUILayout.Space(30);

            GUILayout.Label(m_SelectedSkinnedMesh.localBounds.max.ToString());

            m_UseInternalNamingConvention = EditorGUILayout.Toggle("Internal naming convention", m_UseInternalNamingConvention);

            if (m_UseInternalNamingConvention)
            {
                GUI.enabled = false;
            }
            m_FileName = GUILayout.TextField(m_FileName);
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            GUILayout.Label("Mode: ");
            m_SelectedBakeOptionIndex = EditorGUILayout.Popup(m_SelectedBakeOptionIndex, m_BakeOptionsNames.ToArray());
            GUILayout.Label("Bake Type: ");
            m_SelectedBakeTypeIndex = EditorGUILayout.Popup(m_SelectedBakeTypeIndex, m_BakeTypeNames.ToArray());

            GUILayout.Label("Baker:");
            GUI.enabled = false;
            m_ComputeBaker = (ComputeShader)EditorGUILayout.ObjectField(m_ComputeBaker, typeof(ComputeShader), false);
            GUI.enabled = m_SelectedObject != null;
            if (GUILayout.Button("Bake", GUILayout.Height(50)) && m_SelectedObject != null && m_SelectedAnimation != null && m_SelectedSkinnedMesh != null)
            {
                Bake();
            }
            GUI.enabled = true;
            GUILayout.Space(2.5f);
        }
    }

    void UpdateFileName()
    {
        if (m_PickedAnimations.Count == 0 || m_SelectedObject == null)
        {
            m_FileName = "N/A";
            return;
        }

        string animationFileName = "";
        if (m_PickedAnimations.Count == 1)
        {
            animationFileName = m_PickedAnimations[0].name;
        }
        else
        {
            animationFileName = "bundled";
        }
        m_FileName = string.Format(ASSET_NAME_CONVENTION, m_SelectedObject.name, animationFileName);
    }

    private void Bake()
    {
        if (m_SelectedBakeOptionIndex == 0)
        {
            GPUBake();
        }
        else
        {
            CPUBake();
        }
    }

    private void Init()
    {
        string path = string.Format(COMPUTE_SHADER_PATH, COMPUTE_SHADER_NAME);
        m_ComputeBaker = Resources.Load<ComputeShader>(path);
        ChangeSelected();
        m_Initialized = true;
    }


    private void CPUBake()
    {
        int framesCount = 0;
        for (int i = 0; i < m_PickedAnimations.Count; i++)
        {
            framesCount += (int)(m_PickedAnimations[i].length * m_PickedAnimations[i].frameRate * (1 - m_PickedAnimationsCompressions[i]));
        }

        Vector3[] restPoseVertices = m_SelectedSkinnedMesh.sharedMesh.vertices;

        m_TextureData = new Texture2D(m_SelectedSkinnedMesh.sharedMesh.vertexCount, framesCount, m_RenderTextureFormat, TextureCreationFlags.None);

        Mesh bakedMesh = new Mesh();

        int rowAccumulation = 0;

        for (int a = 0; a < m_PickedAnimations.Count; a++)
        {
            int currenAnimationFramesCount = (int)(m_PickedAnimations[a].length * m_PickedAnimations[a].frameRate * (1 - m_PickedAnimationsCompressions[a]));

            for (int i = 0; i < currenAnimationFramesCount; i++)
            {
                float time = ((float)i / currenAnimationFramesCount) * m_PickedAnimations[a].length;
                m_SelectedAnimation.SampleAnimation(m_SelectedObject, time);

                m_SelectedSkinnedMesh.BakeMesh(bakedMesh);

                FillTextureRow(i + rowAccumulation, bakedMesh.vertices, restPoseVertices);
            }

            rowAccumulation += currenAnimationFramesCount;
        }

        SaveImage();
    }

    void GPUBake()
    {
        int totalFrames = 0;
        for (int i = 0; i < m_PickedAnimations.Count; i++)
        {
            totalFrames += (int)(m_PickedAnimations[i].length * m_PickedAnimations[i].frameRate * (1 - m_PickedAnimationsCompressions[i]));
        }

        m_SelectedSkinnedMesh.quality = SkinQuality.Bone4;
        m_SelectedSkinnedMesh.forceMatrixRecalculationPerRender = true;


        //create RenderTexture
        RenderTexture rt = new RenderTexture(m_SelectedSkinnedMesh.sharedMesh.vertexCount, totalFrames, 0, m_RenderTextureFormat);
        rt.enableRandomWrite = true;

        m_ComputeBaker.SetTexture(m_SelectedBakeTypeIndex, INTERNAL_COMPUTE_TEXTURE_NAME, rt);

        //Set rest pose Data
        ComputeBuffer restPoseBuffer = new ComputeBuffer(m_SelectedSkinnedMesh.sharedMesh.vertexCount, sizeof(float) * 3);
        restPoseBuffer.SetData(m_SelectedSkinnedMesh.sharedMesh.vertices);

        List<ComputeBuffer> computeBuffers = new List<ComputeBuffer>();

        Mesh bakedMesh = new Mesh();

        int rowAccumulation = 0;

        for (int a = 0; a < m_PickedAnimations.Count; a++)
        {
            int currenAnimationFramesCount = (int)(m_PickedAnimations[a].length * m_PickedAnimations[a].frameRate * (1 - m_PickedAnimationsCompressions[a]));

            for (int i = 0; i < currenAnimationFramesCount; i++)
            {
                float time = ((float)i / currenAnimationFramesCount) * m_PickedAnimations[a].length;
                m_PickedAnimations[a].SampleAnimation(m_SelectedObject, time);

                m_SelectedSkinnedMesh.BakeMesh(bakedMesh);

                ComputeBuffer positionsBuffer = new ComputeBuffer(m_SelectedSkinnedMesh.sharedMesh.vertexCount, sizeof(float) * 3);
                computeBuffers.Add(positionsBuffer);

                positionsBuffer.SetData(bakedMesh.vertices);
                m_ComputeBaker.SetBuffer(m_SelectedBakeTypeIndex, "_VertexBuffer", positionsBuffer);
                m_ComputeBaker.SetInt("_Row", i + rowAccumulation);
                m_ComputeBaker.SetBuffer(m_SelectedBakeTypeIndex, "_RestPose", restPoseBuffer);
                m_ComputeBaker.Dispatch(m_SelectedBakeTypeIndex, bakedMesh.vertexCount, 1, 1);
            }

            rowAccumulation += currenAnimationFramesCount;
        }

        //Read from render texture and store to a tex2D
        RenderTexture.active = rt;
        m_TextureData = new Texture2D(rt.width, rt.height, m_RenderTextureFormat, TextureCreationFlags.None);
        m_TextureData.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;

        SaveImage();

        for (int i = 0; i < computeBuffers.Count; i++)
        {
            computeBuffers[i].Release();
            computeBuffers[i] = null;
        }
        restPoseBuffer.Release();
        restPoseBuffer = null;
    }


    void SaveImage()
    {
        if (m_UseInternalNamingConvention)
        {
            m_FileName = string.Format(ASSET_NAME_CONVENTION, m_SelectedObject.name, m_SelectedAnimation.name);
        }

        if (File.Exists(m_FolderPath + m_FileName))
        {
            Object currentTex = AssetDatabase.LoadAssetAtPath<Object>(m_FolderPath + m_FileName);
            EditorUtility.CopySerialized(m_TextureData, currentTex);
        }
        else
        {
            AssetDatabase.CreateAsset(m_TextureData, m_FolderPath + m_FileName);
        }

        AssetDatabase.SaveAssets();

        if (WindowInstance != null)
        {
            EditorUtility.SetDirty(WindowInstance);
        }
    }

    void FillTextureRow(int rowIndex, Vector3[] vertices, Vector3[] restPoseVertices)
    {
        //TODO: Investigate why this and the GPU version even though the calculation is the same returns different outputs
        //p-Here
        if (m_SelectedBakeTypeIndex == 0)
        {
            for (int i = 0; i < m_TextureData.width; i++)
            {
                Vector3 distance = vertices[i] - restPoseVertices[i];
                distance /= 6; //compression
                Color pos = new Color(distance.x * 0.5f + 0.5f, distance.y * 0.5f + 0.5f, distance.z * 0.5f + 0.5f, 0);
                m_TextureData.SetPixel(i, rowIndex, pos);
            }
            return;
        }

        for (int i = 0; i < m_TextureData.width; i++)
        {
            Vector3 position = vertices[i] / 3;
            Color positionColor = new Color(vertices[i].x * 0.5f + 0.5f, vertices[i].y * 0.5f + 0.5f, vertices[i].z * 0.5f + 0.5f, 1);
            m_TextureData.SetPixel(i, rowIndex, positionColor);
        }
    }

    [MenuItem("Tools/Animation Texture Baker")]
    public static void Open()
    {
        WindowInstance = GetWindow<AnimationTextureBakerWindow>();
        WindowInstance.titleContent.tooltip = "Animation Texture Baker";
        WindowInstance.titleContent.text = "Coso che cosa il coso";
        WindowInstance.autoRepaintOnSceneChange = true;
        WindowInstance.Show();
    }

    private void ChangeSelected()
    {
        if (m_SelectedObject == null) return;

        m_Path = AssetDatabase.GetAssetPath(m_SelectedObject);
        if (m_Path.ToLower().EndsWith(".fbx"))
        {
            m_FolderPath = GetFolderPath(m_Path);

            m_AnimationsNames.Clear();
            m_PickedAnimations.Clear();
            m_PickedAnimationsCompressions.Clear();
            m_SelectedAnimationIndex = 0;
            UpdateFileName();

            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(m_Path);
            m_Animations = allAssets.Where(x => x.GetType() == typeof(AnimationClip) && !x.name.StartsWith("__preview")).ToList();

            for (int i = 0; i < m_Animations.Count; i++)
            {
                m_AnimationsNames.Add(m_Animations[i].name);
            }

            if (m_Animations.Count > 0)
            {
                m_SelectedAnimation = m_Animations[m_SelectedAnimationIndex] as AnimationClip;
                //we assume that if the mesh has animation it will also have a SkinnedMeshRenderer
                m_SelectedSkinnedMesh = m_SelectedObject.GetComponentInChildren<SkinnedMeshRenderer>();
                if (m_SelectedSkinnedMesh != null)
                    m_SelectedSkinnedMesh.quality = SkinQuality.Bone4;
            }
        }
    }

    public static string GetFolderPath(string filePath)
    {
        string[] splittedPath = filePath.Split('/');

        if (splittedPath.Length == 0) return null;

        string folderPath = "";
        for (int i = 0; i < splittedPath.Length - 1; i++)
        {
            folderPath += splittedPath[i] + "/";
        }
        return folderPath;
    }
}
