using Microsoft.International.Converters.PinYinConverter;
using PhotoshopFile;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class PSD2UGUIEditor : EditorWindow
{
    private string _psdFilePath="";
    private string _exportAssetPath = "Assets";
    //private PsdFile _psdFile;
    private LayerTreeView _layerTreeView;
    [SerializeField]
    private TreeViewState _treeViewState;
    private float pixelsToUnitSize = 100.0f;

    private Dictionary<string, string> _uniqueSprites;


    [MenuItem("Tools/PSD2UGUIEditor")]
    static void Main()
    {
        GetWindow<PSD2UGUIEditor>();
    }

    private void OnEnable()
    {
        _treeViewState = new TreeViewState();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void OnGUI()
    {
        GUILayout.Label(_psdFilePath);
        if (GUILayout.Button("Select psd file"))
        {
            _psdFilePath = EditorUtility.OpenFilePanel("Select psd file", "", "psd");
            if (!string.IsNullOrEmpty(_psdFilePath)
                && File.Exists(_psdFilePath))
            {
                var psdFile = new PsdFile(_psdFilePath,Encoding.Default);
                var layerNodeRoot = GetLayerNodeRoot(psdFile,Path.GetFileNameWithoutExtension(_psdFilePath));
                _layerTreeView = new LayerTreeView(_treeViewState, layerNodeRoot);
            }
        }
        _layerTreeView?.OnGUI(new Rect(0, 100, Screen.width, Screen.height-100));
        if (_layerTreeView != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(_exportAssetPath);
            if (GUILayout.Button("Select export path"))
            {
                string exportAssetPath = EditorUtility.OpenFolderPanel("Select export path", _exportAssetPath, "");
                if (!string.IsNullOrEmpty(exportAssetPath))
                {
                    exportAssetPath = Path.GetFullPath(exportAssetPath).Replace(Path.GetFullPath(Application.dataPath), "");
                    exportAssetPath ="Assets"+exportAssetPath.Replace("\\", "/");
                    if (Directory.Exists(exportAssetPath))
                    {
                        _exportAssetPath = exportAssetPath;
                    }
                }
            }

            if (GUILayout.Button("Export sprites"))
            {
                ExportSprites();
            }

            if (GUILayout.Button("Export sprites & Create prefab"))
            {
                ExportSprites();
            }
            GUILayout.EndHorizontal();
        }
    }

    private void ExportSprites()
    {
        if (!string.IsNullOrEmpty(_exportAssetPath) && Directory.Exists(_exportAssetPath))
        {
            _uniqueSprites = new Dictionary<string, string>();
            ExportLayerNode(_layerTreeView.LayerNodeRoot);
        }
    }


    private void ExportLayerNode(LayerNode layerNode)
    {
        if (layerNode == null)
            return;

        if (layerNode.Layer != null)
        {
            Texture2D tex = CreateTexture(layerNode.Layer);
            if (tex != null)
            {
                var sprite = SaveAsset(tex, layerNode.Layer.Name);
                layerNode.Sprite = sprite;
                DestroyImmediate(tex);
            }
        }
        
        foreach (var item in layerNode.Children)
        {
            ExportLayerNode(item);
        }
    }

    private LayerNode GetLayerNodeRoot(PsdFile psdFile,string rootName=null)
    {
        List<Layer> layers = new List<Layer>(psdFile.Layers.ToArray());
        layers.Reverse();
        Stack<LayerNode> groupLayerNode = new Stack<LayerNode>();
        LayerNode layerNodeRoot = new LayerNode() { DisplayName =string.IsNullOrEmpty(rootName)?"Root": rootName, Id = 0 };
        groupLayerNode.Push(layerNodeRoot);
        for (int i = 0; i < layers.Count; i++)
        {
            Layer layer = layers[i];
            LayerNode layerNode = new LayerNode();
            layerNode.Id = i+1;
            layerNode.DisplayName = layer.Name;
            layerNode.Layer = layer;

            LayerSectionInfo sectionInfo = GetLayerSectionInfo(layer);
            if (sectionInfo != null)
            {
                if (sectionInfo.SectionType == LayerSectionType.SectionDivider)
                {
                    groupLayerNode.Pop();
                    continue;
                }
                else if (sectionInfo.SectionType == LayerSectionType.OpenFolder || sectionInfo.SectionType == LayerSectionType.ClosedFolder)
                {
                    groupLayerNode.Peek().Children.Add(layerNode);
                    groupLayerNode.Push(layerNode);
                }
            }
            else
            {
                groupLayerNode.Peek().Children.Add(layerNode);
            }
        }
        
        return layerNodeRoot;
    }

    private LayerSectionInfo GetLayerSectionInfo(Layer layer)
    {
        var additionalInfo = layer.AdditionalInfo.Find(x => x.GetType() == typeof(LayerSectionInfo));
        if (additionalInfo != null)
        {

            return additionalInfo as LayerSectionInfo;
        }
        return null;
    }

    private Texture2D CreateTexture(Layer layer)
    {
        if ((int)layer.Rect.width == 0 || (int)layer.Rect.height == 0)
            return null;

        Texture2D tex = new Texture2D((int)layer.Rect.width, (int)layer.Rect.height, TextureFormat.RGBA32, true);
        Color32[] pixels = new Color32[tex.width * tex.height];

        Channel red = (from l in layer.Channels where l.ID == 0 select l).First();
        Channel green = (from l in layer.Channels where l.ID == 1 select l).First();
        Channel blue = (from l in layer.Channels where l.ID == 2 select l).First();
        Channel alpha = layer.AlphaChannel;

        for (int i = 0; i < pixels.Length; i++)
        {
            byte r = red.ImageData[i];
            byte g = green.ImageData[i];
            byte b = blue.ImageData[i];
            byte a = 255;

            if (alpha != null)
                a = alpha.ImageData[i];

            int mod = i % tex.width;
            int n = ((tex.width - mod - 1) + i) - mod;
            pixels[pixels.Length - n - 1] = new Color32(r, g, b, a);
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    private Sprite SaveAsset(Texture2D tex, string suffix)
    {
        string path = Path.Combine(_exportAssetPath, $"{suffix}.png");
        string dir = Path.GetDirectoryName(path);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        byte[] buf = tex.EncodeToPNG();
        File.WriteAllBytes(path, buf);
        var md5 = GetFileMD5(path);
        if (_uniqueSprites.ContainsKey(md5))
        {
            var oldSpritePath = _uniqueSprites[md5];
            Debug.Log($"Existing sprite: {md5}  {oldSpritePath} & {path}");
            File.Delete(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(oldSpritePath);
        }
        AssetDatabase.Refresh();
        // Load the texture so we can change the type
        AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D));
        TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
        textureImporter.textureType = TextureImporterType.Sprite;
        textureImporter.spriteImportMode = SpriteImportMode.Single;
        textureImporter.spritePivot = new Vector2(0.5f, 0.5f);
        textureImporter.spritePixelsPerUnit = pixelsToUnitSize;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var sprite = (Sprite)AssetDatabase.LoadAssetAtPath(path, typeof(Sprite));
        //Debug.Log($"文件md5: {md5} {path}");
        _uniqueSprites.Add(md5, AssetDatabase.GetAssetPath(sprite));
        return sprite;
    }

    //获取文件的md5值
    public string GetFileMD5(string filePath)
    {
        try
        {
            using (var fileStream = File.OpenRead(filePath))
            {
                System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
                byte[] toData = md5.ComputeHash(fileStream);
                string fileMD5 = BitConverter.ToString(toData).Replace("-", "").ToLower();
                return fileMD5;
            }
        }
        catch (Exception e)
        {
            Debug.Log($"{filePath}  {e}");
            return filePath;
        }
    }

    //获取文件的md5值
    public string GetFileMD5(byte[] data)
    {
        System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
        byte[] toData = md5.ComputeHash(data);
        string fileMD5 = BitConverter.ToString(toData).Replace("-", "").ToLower();
        return fileMD5;
    }
}

class LayerTreeView : TreeView
{
    public LayerNode LayerNodeRoot { get; private set; }
    public LayerTreeView(TreeViewState state, LayerNode layerNode) :base(state)
    {
        LayerNodeRoot = layerNode;
        Reload();
    }
    protected override TreeViewItem BuildRoot()
    {
        if (LayerNodeRoot != null)
        {
            var root = GetTreeViewItem(LayerNodeRoot);
            root.depth = -1;
            SetupDepthsFromParentsAndChildren(root);
            return root;
        }
        return null;
    }

    private TreeViewItem GetTreeViewItem(LayerNode layerNode)
    {
        TreeViewItem viewItem = new TreeViewItem();
        viewItem.id = layerNode.Id;
        viewItem.displayName = layerNode.DisplayName;
        foreach (var item in layerNode.Children)
        {
            var childTreeViewItem = GetTreeViewItem(item);
            viewItem.AddChild(childTreeViewItem);
        }
        return viewItem;
    }
}

class LayerNode
{
    public int Id;
    public string DisplayName;
    public Layer Layer;
    public Sprite Sprite;
    public List<LayerNode> Children=new List<LayerNode>();
}
