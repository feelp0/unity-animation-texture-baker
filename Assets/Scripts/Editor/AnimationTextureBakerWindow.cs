using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

[ExecuteAlways]
public class AnimationTextureBakerWindow : EditorWindow
{
    public static AnimationTextureBakerWindow WindowInstance;
    GameObject selectedObject;
    bool lockSelected;
    bool allowEveryObject = false;
    List<Object> animations = new List<Object>();
    List<string> animationsNames = new List<string>();
    AnimationClip selectedAnimation = null;
    List<AnimationClip> selectedAnimations = new List<AnimationClip>();
    List<float> selectedAnimationsCompressions = new List<float>();
    SkinnedMeshRenderer selectedSkinnedMesh;
    int selectedAnimationIndex = 0;

    string path = "";
    string folderPath = "";

    bool initialized = false;

    float animationCompression = 0;

    private const string CURR_SELECT_LABEL = "Selected FBX: {0}";

    private const string COMPUTE_SHADER_PATH = "Editor/Shaders/{0}";
    private const string COMPUTE_SHADER_NAME = "AnimationTextureBaker";
    ComputeShader computeBaker = null;

    int selectedBakeOptionIndex = 0;
    string[] bakeOptionsNames = new string[] { "GPU", "CPU" };

    private void OnGUI()
    {
        if (!initialized)
        {
            Init();
        }
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUI.BeginChangeCheck();
            allowEveryObject = GUILayout.Toggle(allowEveryObject, "Allow Every Object");
            if (EditorGUI.EndChangeCheck())
            {
                OnSelectionChange();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUIContent lockTex = lockSelected ? EditorGUIUtility.IconContent("LockIcon-On") : EditorGUIUtility.IconContent("LockIcon");
                //lockSelected = GUILayout.Toggle(lockSelected, lockTex);
                if (GUILayout.Button(lockTex, GUILayout.Width(25)))
                {
                    lockSelected = !lockSelected;
                }
                //Call on selection change when we untoggle lockSelected
                //if (EditorGUI.EndChangeCheck() && lockSelected == false)
                //{
                //    OnSelectionChange();
                //}
                string objName = selectedObject == null ? "N/A" : selectedObject.name;
                EditorGUILayout.LabelField(string.Format(CURR_SELECT_LABEL, objName));
                EditorGUI.BeginChangeCheck();
            }

            //if (allowEveryObject)
            //{
            //    selectedAnimation = (AnimationClip)EditorGUILayout.ObjectField(selectedAnimation, typeof(AnimationClip), false);
            //}

            if (animationsNames.Count > 0)
            {
                GUILayout.Space(5);
                GUILayout.Label("Animations:");
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    selectedAnimationIndex = EditorGUILayout.Popup(selectedAnimationIndex, animationsNames.ToArray());
                    if (EditorGUI.EndChangeCheck() && animations.Count > 0)
                    {
                        selectedAnimation = animations[selectedAnimationIndex] as AnimationClip;
                    }
                    if (GUILayout.Button("+"))
                    {
                        selectedAnimations.Add(selectedAnimation);
                        selectedAnimationsCompressions.Add(animationCompression);
                    }
                }
            }

            animationCompression = EditorGUILayout.Slider("Clip compression: ", animationCompression, 0, 1);

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Picked Animations: ");
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(selectedAnimations.Count.ToString());
                }
                for (int i = 0; i < selectedAnimations.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUI.enabled = false;
                        EditorGUILayout.ObjectField(selectedAnimations[i], typeof(AnimationClip), false);
                        GUI.enabled = true;
                        selectedAnimationsCompressions[i] = EditorGUILayout.Slider(selectedAnimationsCompressions[i], 0, 1, GUILayout.MaxWidth(120));
                        if (GUILayout.Button("-"))
                        {
                            selectedAnimations.RemoveAt(i);
                            selectedAnimationsCompressions.RemoveAt(i);
                        }
                    }
                }
            }

            EditorGUILayout.Space(10);
            //put this on change check
            if (selectedAnimation != null && selectedSkinnedMesh != null && selectedObject != null)
            {
                int framesCount = 0;
                for (int i = 0; i < selectedAnimations.Count; i++)
                {
                    framesCount += (int)(selectedAnimations[i].length * selectedAnimations[i].frameRate * (1 - selectedAnimationsCompressions[i]));
                }
                string expectedSize = string.Format("Expected size: {0}x{1}", selectedSkinnedMesh.sharedMesh.vertexCount, framesCount);
                EditorGUILayout.LabelField(expectedSize);
                EditorGUILayout.LabelField("Vertices: " + selectedSkinnedMesh.sharedMesh.vertexCount);
                EditorGUILayout.LabelField("Total Frames: " + framesCount);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Mode: ");
            selectedBakeOptionIndex = EditorGUILayout.Popup(selectedBakeOptionIndex, bakeOptionsNames.ToArray());

            GUILayout.Label("Baker:");
            GUI.enabled = false;
            computeBaker = (ComputeShader)EditorGUILayout.ObjectField(computeBaker, typeof(ComputeShader), false);
            GUI.enabled = true;
            if (GUILayout.Button("Bake", GUILayout.Height(50)) && selectedObject != null && selectedAnimation != null && selectedSkinnedMesh != null)
            {
                Bake();
            }

            GUILayout.Space(2.5f);
        }
    }

    private void Bake()
    {
        if (selectedBakeOptionIndex == 0)
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
        computeBaker = Resources.Load<ComputeShader>(path);
        OnSelectionChange();
        initialized = true;
    }

    Texture2D tempVertexBuffer;

    private void CPUBake()
    {
        int framesCount = 0;
        for (int i = 0; i < selectedAnimations.Count; i++)
        {
            framesCount += (int)(selectedAnimations[i].length * selectedAnimations[i].frameRate * (1 - selectedAnimationsCompressions[i]));
        }

        //int framesCount = (int)(selectedAnimation.length * selectedAnimation.frameRate * (1 - animationCompression));

        ComputeBuffer restPoseBuffer = new ComputeBuffer(selectedMesh.vertexCount, sizeof(float) * 3);
        restPoseBuffer.SetData(selectedMesh.vertices);
        computeBaker.SetBuffer(0, "_RestPose", restPoseBuffer);

        tempVertexBuffer = new Texture2D(selectedSkinnedMesh.sharedMesh.vertexCount, framesCount,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
            UnityEngine.Experimental.Rendering.TextureCreationFlags.None);

        Mesh bakedMesh = new Mesh();

        int rowAccumulation = 0;

        for (int a = 0; a < selectedAnimations.Count; a++)
        {
            int currenAnimationFramesCount = (int)(selectedAnimations[a].length * selectedAnimations[a].frameRate * (1 - selectedAnimationsCompressions[a]));

            for (int i = 0; i < currenAnimationFramesCount; i++)
            {
                float time = ((float)i / currenAnimationFramesCount) * selectedAnimations[a].length;
                selectedAnimation.SampleAnimation(selectedObject, time);

                selectedSkinnedMesh.BakeMesh(bakedMesh);

                FillTextureRow(i + rowAccumulation, bakedMesh.vertices);
            }

            rowAccumulation += currenAnimationFramesCount;
        }

        SaveImage();
    }

    private const string INTERNAL_COMPUTE_TEXTURE_NAME = "_Out";

    void GPUBake()
    {
        int totalFrames = 0;
        for (int i = 0; i < selectedAnimations.Count; i++)
        {
            totalFrames += (int)(selectedAnimations[i].length * selectedAnimations[i].frameRate * (1 - selectedAnimationsCompressions[i]));
        }

        //create RenderTexture
        GraphicsFormat textureFormat = GraphicsFormat.R16G16B16A16_SFloat;
        RenderTexture rt = new RenderTexture(selectedSkinnedMesh.sharedMesh.vertexCount, totalFrames, 0, textureFormat);
        rt.enableRandomWrite = true;

        computeBaker.SetTexture(0, INTERNAL_COMPUTE_TEXTURE_NAME, rt);


        if (selectedMesh == null)
        {
            Debug.LogError("No Mesh Found on selected Assets");
            return;
        }

        //Set rest pose Data
        ComputeBuffer restPoseBuffer = new ComputeBuffer(selectedMesh.vertexCount, sizeof(float) * 3);
        restPoseBuffer.SetData(selectedMesh.vertices);
        computeBaker.SetBuffer(0, "_RestPose", restPoseBuffer);

        ComputeBuffer positionsBuffer = new ComputeBuffer(selectedSkinnedMesh.sharedMesh.vertexCount, sizeof(float) * 3);

        Mesh bakedMesh = new Mesh();

        int rowAccumulation = 0;

        for (int a = 0; a < selectedAnimations.Count; a++)
        {
            int currenAnimationFramesCount = (int)(selectedAnimations[a].length * selectedAnimations[a].frameRate * (1 - selectedAnimationsCompressions[a]));

            for (int i = 0; i < currenAnimationFramesCount; i++)
            {
                float time = ((float)i / currenAnimationFramesCount) * selectedAnimations[a].length;
                selectedAnimations[a].SampleAnimation(selectedObject, time);

                selectedSkinnedMesh.BakeMesh(bakedMesh);

                positionsBuffer.SetData(bakedMesh.vertices);
                computeBaker.SetBuffer(0, "_VertexBuffer", positionsBuffer);
                computeBaker.SetInt("_Row", i + rowAccumulation);
                computeBaker.Dispatch(0, bakedMesh.vertexCount, 1, 1);
            }

            rowAccumulation += currenAnimationFramesCount;
        }

        //Read from render texture and store to a tex2D
        RenderTexture.active = rt;
        tempVertexBuffer = new Texture2D(rt.width, rt.height, textureFormat, TextureCreationFlags.None);
        tempVertexBuffer.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;

        SaveImage();

        positionsBuffer.Release();
        positionsBuffer = null;
        restPoseBuffer.Release();
        restPoseBuffer = null;
    }

    void SaveImage()
    {
        string extension = ".asset";
        string name = string.Format("{0}_{1}_AnimationTexture" + extension, selectedObject.name, selectedAnimation.name);
        if (File.Exists(folderPath + name + extension))
        {
            Object currentTex = AssetDatabase.LoadAssetAtPath<Object>(folderPath + name);
            EditorUtility.CopySerialized(tempVertexBuffer, currentTex);
        }
        else
        {
            AssetDatabase.CreateAsset(tempVertexBuffer, folderPath + name);
        }

        AssetDatabase.SaveAssets();

        if (WindowInstance != null)
        {
            EditorUtility.SetDirty(WindowInstance);
        }
    }

    void FillTextureRow(int rowIndex, Vector3[] data)
    {
        for (int x = 0; x < tempVertexBuffer.width; x++)
        {
            Color pos = new Color(data[x].x, data[x].y, data[x].z, 0);
            tempVertexBuffer.SetPixel(x, rowIndex, pos);
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

    Mesh selectedMesh = null;

    private void OnSelectionChange()
    {
        if (lockSelected) return;

        path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (path.ToLower().EndsWith(".fbx") || allowEveryObject)
        {
            folderPath = GetFolderPath(path);

            animationsNames.Clear();
            try
            {
                selectedObject = (GameObject)Selection.activeObject;
            }
            catch
            {
                selectedObject = null;
                selectedAnimation = null;
                selectedSkinnedMesh = null;
                return;
            }

            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            animations = allAssets.Where(x => x.GetType() == typeof(AnimationClip) && !x.name.StartsWith("__preview")).ToList();

            List<Object> allMeshes = allAssets.Where(x => x.GetType() == typeof(Mesh)).ToList();
            if (allMeshes.Count > 0)
            {
                selectedMesh = allMeshes[0] as Mesh;
            }

            for (int i = 0; i < animations.Count; i++)
            {
                animationsNames.Add(animations[i].name);
            }

            selectedAnimationIndex = 0;
            if (animations.Count > 0)
            {
                selectedAnimation = animations[selectedAnimationIndex] as AnimationClip;
                //we assume that if the mesh has animation it will also have a SkinnedMeshRenderer
                selectedSkinnedMesh = selectedObject.GetComponentInChildren<SkinnedMeshRenderer>();
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
