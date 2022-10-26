using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

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
                if (EditorGUI.EndChangeCheck() && lockSelected == false)
                {
                    OnSelectionChange();
                }
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
                }
            }

            animationCompression = EditorGUILayout.Slider("Clip compression: ", animationCompression, 0, 1);

            EditorGUILayout.Space(10);
            //put this on change check
            if (selectedAnimation != null && selectedSkinnedMesh != null && selectedObject != null)
            {
                int framesCount = (int)(selectedAnimation.length * selectedAnimation.frameRate * (1 - animationCompression));
                string expectedSize = string.Format("Expected size: {0}x{1}", selectedSkinnedMesh.sharedMesh.vertexCount, framesCount);
                EditorGUILayout.LabelField(expectedSize);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Baker:");
            GUI.enabled = false;
            computeBaker = (ComputeShader)EditorGUILayout.ObjectField(computeBaker, typeof(ComputeShader), false);
            GUI.enabled = true;
            if (GUILayout.Button("Bake", GUILayout.Height(50)) && selectedObject != null && selectedAnimation != null && selectedSkinnedMesh != null)
            {
                StartBake();
            }

            GUILayout.Space(2.5f);
        }
    }

    private void Init()
    {
        string path = string.Format(COMPUTE_SHADER_PATH, COMPUTE_SHADER_NAME);
        Debug.Log(path);
        computeBaker = Resources.Load<ComputeShader>(path);
        OnSelectionChange();
        initialized = true;
    }

    Texture2D tempVertexBuffer;

    private void StartBake()
    {
        int framesCount = (int)(selectedAnimation.length * selectedAnimation.frameRate * (1 - animationCompression));

        //For the time beeing idk about texture size, i just want it running. Atm I'll store everything in this one 
        //TODO: Optimize
        tempVertexBuffer = new Texture2D(selectedSkinnedMesh.sharedMesh.vertexCount, framesCount,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat,
            UnityEngine.Experimental.Rendering.TextureCreationFlags.None);


        Mesh bakedMesh = new Mesh();

        for (int i = 0; i < framesCount; i++)
        {
            float time = ((float)i / framesCount) * selectedAnimation.length;
            selectedAnimation.SampleAnimation(selectedObject, time);

            //possible alternatives: save pos diff (paulo solution), save it in scrSpace and remap? probably won't work, 
            selectedSkinnedMesh.BakeMesh(bakedMesh);
            Debug.Log(bakedMesh.vertices[0]);

            FillTextureRow(i, bakedMesh.vertices);
        }

        //TODO: find why this is not overwriting. Destroying and re-create works but shader loses refs to texture.
        //maybe i can try copy the GUID?
        string name = string.Format("{0}_{1}_AnimationTexture.asset", selectedObject.name, selectedAnimation.name);
        if (File.Exists(folderPath + name))
        {
            AssetDatabase.DeleteAsset(folderPath + name);
        }
        AssetDatabase.CreateAsset(tempVertexBuffer, folderPath + name);

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(WindowInstance);

        //byte[] imageData = tempVertexBuffer.EncodeToEXR();
        //File.WriteAllBytes(folderPath + "bakedResult.asset", imageData);
    }

    RenderTexture myLittleBuffer;
    private const string INTERNAL_COMPUTE_TEXTURE_NAME = "Result";

    void GPUBake()
    {
        int framesCount = (int)(selectedAnimation.length * selectedAnimation.frameRate * (1 - animationCompression));

        myLittleBuffer = new RenderTexture(selectedSkinnedMesh.sharedMesh.vertexCount, framesCount, 0,
            UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat);

        computeBaker.SetTexture(0, INTERNAL_COMPUTE_TEXTURE_NAME, myLittleBuffer);


        Mesh bakedMesh = new Mesh();

        Vector3[,] framesVertices = new Vector3[selectedSkinnedMesh.sharedMesh.vertexCount, framesCount];
        //List<Vector3[]> framesVertices = new List<Vector3[]>();
        //TODO: should i dispatch every row or store and dispatch only once?
        //Porbably dispatching per row is a better idea

        for (int i = 0; i < framesCount; i++)
        {
            float time = ((float)i / framesCount) * selectedAnimation.length;
            selectedAnimation.SampleAnimation(selectedObject, time);

            selectedSkinnedMesh.BakeMesh(bakedMesh);

            //FillTextureRow(i, bakedMesh.vertices);
            //framesVertices.Add()
        }

        //computeBaker.SetVectorArray()
        //computeBaker.Dispatch()
    }

    //TODO: Once finished merge GPU and CPU bake in this only function and switch based on the input bake type
    void Bake()
    {
        //GetFrames

        //Switch

        //Bake

        //Store (GPU)

        //Save
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
