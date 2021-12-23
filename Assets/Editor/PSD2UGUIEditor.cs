using Microsoft.International.Converters.PinYinConverter;
using PhotoshopFile;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

public class PSD2UGUIEditor : EditorWindow
{
    private string _psdFilePath = "";
    private string _exportAssetPath = "Assets";
    //private PsdFile _psdFile;
    private LayerTreeView _layerTreeView;
    [SerializeField]
    private TreeViewState _treeViewState;
    private float pixelsToUnitSize = 100.0f;

    private Dictionary<string, string> _uniqueSprites;
    private Vector2 _psdBaseLayerSize;

    [MenuItem("Tools/PSD2UGUIEditor")]
    static void Main()
    {
        GetWindow<PSD2UGUIEditor>();
    }

    private void OnEnable()
    {
        _treeViewState = new TreeViewState();
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
                var psdFile = new PsdFile(_psdFilePath, Encoding.Default);
                var layerNodeRoot = GetLayerNodeRoot(psdFile, Path.GetFileNameWithoutExtension(_psdFilePath));
                _layerTreeView = new LayerTreeView(_treeViewState, layerNodeRoot);
            }
        }

        _layerTreeView?.OnGUI(new Rect(0, 100, Screen.width, Screen.height));
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
                    exportAssetPath = "Assets" + exportAssetPath.Replace("\\", "/");
                    if (Directory.Exists(exportAssetPath))
                    {
                        _exportAssetPath = exportAssetPath;
                    }
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Export sprites"))
            {
                ExportSprites();
            }

            if (GUILayout.Button("Export sprites & Create prefab"))
            {
                ExportSprites();
                CreatePrefab(_layerTreeView.LayerNodeRoot);
            }
            GUILayout.EndHorizontal();
        }
    }

    //导出Sprite
    private void ExportSprites()
    {
        if (!string.IsNullOrEmpty(_exportAssetPath) && Directory.Exists(_exportAssetPath))
        {
            _uniqueSprites = new Dictionary<string, string>();
            ExportLayerNode(_layerTreeView.LayerNodeRoot);
        }
    }

    private void CreatePrefab(LayerNode layerNode)
    {
        var rootRectTransform = CreateRectTransform(layerNode);
        string path = $"{_exportAssetPath}/{layerNode.EnglishName}.prefab";
        PrefabUtility.CreatePrefab(path, rootRectTransform.gameObject);
        DestroyImmediate(rootRectTransform.gameObject);
        AssetDatabase.Refresh();
    }

    private RectTransform CreateRectTransform(LayerNode layerNode, RectTransform parent = null)
    {
        var rectTrans = new GameObject(layerNode.EnglishName).AddComponent<RectTransform>();
        if (parent != null)
        {
            rectTrans.SetParent(parent);
        }

        rectTrans.localScale = Vector3.one;
        rectTrans.localPosition = Vector3.zero;

        if (!string.IsNullOrEmpty(layerNode.SpritePath))
        {
            rectTrans.gameObject.AddComponent<Image>().sprite = AssetDatabase.LoadAssetAtPath<Sprite>(layerNode.SpritePath);
        }

        if (layerNode.Layer != null && layerNode.Layer.Rect != Rect.zero)
        {
            var layer = layerNode.Layer;
            rectTrans.sizeDelta = layer.Rect.size;
            float positionX = layer.Rect.x - _psdBaseLayerSize.x * 0.5f + layer.Rect.width * 0.5f;
            float positionY = _psdBaseLayerSize.y * 0.5f - layer.Rect.y - layer.Rect.height * 0.5f;
            rectTrans.anchoredPosition = new Vector2(positionX, positionY);
        }
        else
        {
            rectTrans.sizeDelta = _psdBaseLayerSize;
        }

        //倒序创建
        for (int i = layerNode.Children.Count - 1; i >= 0; i--)
        {
            CreateRectTransform(layerNode.Children[i], rectTrans);
        }

        return rectTrans;
    }


    private void ExportLayerNode(LayerNode layerNode)
    {
        if (layerNode == null)
            return;

        //转换为英文名称
        string layerNodeName = layerNode.DisplayName;
        layerNodeName = CheckName(layerNodeName);
        var englishName = GetEnglishName(layerNodeName);
        englishName = CheckName(englishName, "_");
        layerNode.EnglishName = string.IsNullOrEmpty(englishName) ? layerNode.DisplayName : englishName;

        if (layerNode.Layer != null)
        {
            Texture2D tex = CreateTexture(layerNode.Layer);
            if (tex != null)
            {
                //获取对应的sprite
                var spritePath = SaveAsset(tex, englishName);
                layerNode.SpritePath = spritePath;
                DestroyImmediate(tex);
            }
        }

        foreach (var item in layerNode.Children)
        {
            ExportLayerNode(item);
        }
    }

    private LayerNode GetLayerNodeRoot(PsdFile psdFile, string rootName = null)
    {
        _psdBaseLayerSize = psdFile.BaseLayer.Rect.size;

        List<Layer> layers = new List<Layer>(psdFile.Layers.ToArray());
        layers.Reverse();
        Stack<LayerNode> groupLayerNode = new Stack<LayerNode>();
        LayerNode layerNodeRoot = new LayerNode();
        layerNodeRoot.Id = 0;
        //layerNodeRoot.Layer = psdFile.BaseLayer;
        layerNodeRoot.DisplayName = "LayerRoot";
        groupLayerNode.Push(layerNodeRoot);
        for (int i = 0; i < layers.Count; i++)
        {
            Layer layer = layers[i];
            LayerNode layerNode = new LayerNode();
            layerNode.Id = i + 1;
            layerNode.DisplayName = string.IsNullOrEmpty(layer.Name) ? $"Layer{layerNode.Id}" : layer.Name;
            layerNode.Layer = layer;

            Debug.Log($"{layer.Name} {layer.Rect}");
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

    private string SaveAsset(Texture2D tex, string suffix)
    {
        foreach (var item in _uniqueSprites.Values)
        {
            if (item.EndsWith($"{suffix}.png"))
            {
                suffix += "_" + Guid.NewGuid().ToString().Substring(0, 6);
                break;
            }
        }

        string path = Path.Combine(_exportAssetPath, $"Sprites/{suffix}.png");
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
            return oldSpritePath;
        }
        AssetDatabase.Refresh();
        // Load the texture so we can change the type
        AssetDatabase.LoadAssetAtPath(path, typeof(Texture2D));
        TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
        if (textureImporter != null)
        {
            textureImporter.textureType = TextureImporterType.Sprite;
            textureImporter.spriteImportMode = SpriteImportMode.Single;
            textureImporter.spritePivot = new Vector2(0.5f, 0.5f);
            textureImporter.spritePixelsPerUnit = pixelsToUnitSize;
        }
        else
        {
            Debug.LogWarning($"textureImporter is null! {path}");
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var sprite = (Sprite)AssetDatabase.LoadAssetAtPath(path, typeof(Sprite));
        //Debug.Log($"文件md5: {md5} {path}");
        _uniqueSprites.Add(md5, path);
        return path;
    }

    //获取英文名称
    private string GetEnglishName(string name)
    {
        if (Regex.IsMatch(name, @"[\u4e00-\u9fa5]"))
        {
            var responseContent = Youdao(name).Replace("\n", "").Replace("\r", "").Replace(" ", "");
            if (!string.IsNullOrEmpty(responseContent))
            {
                string regexPattern = ",\"translation\":\\[\"(.+)\"\\],";
                var match = Regex.Match(responseContent, regexPattern, RegexOptions.Singleline);
                if (match.Success && match.Groups != null)
                {
                    var matchGroups = match.Groups;
                    if (matchGroups.Count > 1)
                    {
                        responseContent = match.Groups[1].Value;
                        if (!string.IsNullOrEmpty(responseContent))
                        {
                            return responseContent;
                        }
                    }
                }
            }
            var pinyin = GetPinyin(name);
            Debug.LogWarning($"获取英文名称失败: {name} => {responseContent} => {pinyin}");
            return pinyin;
        }
        return name;
    }

    private string GetPinyin(string name)
    {
        StringBuilder stringBuilder = new StringBuilder();
        foreach (var item in name)
        {
            if (item >= 0x4e00 && item <= 0x9fbb)
            {
                ChineseChar cc = new ChineseChar(item);
                stringBuilder.Append(cc.Pinyins.FirstOrDefault());
            }
            else
            {
                stringBuilder.Append(item);
            }
        }
        return name;
    }

    //去掉名称中的特殊符号以及多余的空格
    private string CheckName(string name, string replace = "")
    {
        name = Regex.Replace(name.Trim(), @"[^a-zA-Z0-9\u4e00-\u9fa5\s]", "").Trim().Replace(" ", replace).Trim();
        return name;
    }

    //获取文件的md5值
    private string GetFileMD5(string filePath)
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
    private string GetFileMD5(byte[] data)
    {
        System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
        byte[] toData = md5.ComputeHash(data);
        string fileMD5 = BitConverter.ToString(toData).Replace("-", "").ToLower();
        return fileMD5;
    }

    private bool MyRemoteCertificateValidationCallback(System.Object sender,
    X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        bool isOk = true;
        // If there are errors in the certificate chain,
        // look at each error to determine the cause.
        if (sslPolicyErrors != SslPolicyErrors.None)
        {
            for (int i = 0; i < chain.ChainStatus.Length; i++)
            {
                if (chain.ChainStatus[i].Status == X509ChainStatusFlags.RevocationStatusUnknown)
                {
                    continue;
                }
                chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                bool chainIsValid = chain.Build((X509Certificate2)certificate);
                if (!chainIsValid)
                {
                    isOk = false;
                    break;
                }
            }
        }
        return isOk;
    }


    private string Youdao(string queryText)
    {
        Dictionary<String, String> dic = new Dictionary<String, String>();
        string url = "https://openapi.youdao.com/api";
        string appKey = "0d8ce13fe65cc10e";
        string appSecret = "NjxbXZVwT7Gi5v70NApIbBOpq8pi8o4A";
        string salt = Guid.NewGuid().ToString();//DateTime.Now.Millisecond.ToString()
        dic.Add("from", "auto");
        dic.Add("to", "auto");
        dic.Add("signType", "v3");
        TimeSpan ts = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        long millis = (long)ts.TotalMilliseconds;
        string curtime = Convert.ToString(millis / 1000);
        dic.Add("curtime", curtime);
        string signStr = appKey + YoudaoTruncate(queryText) + salt + curtime + appSecret; ;
        string sign = YoudaoComputeHash(signStr, new SHA256CryptoServiceProvider());
        dic.Add("q", Uri.EscapeDataString(queryText));
        dic.Add("appKey", appKey);
        dic.Add("salt", salt);
        dic.Add("sign", sign);
        return YoudaoPost(url, dic);
    }

    private static string YoudaoTruncate(string q)
    {
        if (q == null)
        {
            return null;
        }
        int len = q.Length;
        return len <= 20 ? q : (q.Substring(0, 10) + len + q.Substring(len - 10, 10));
    }
    private string YoudaoComputeHash(string input, HashAlgorithm algorithm)
    {
        Byte[] inputBytes = Encoding.UTF8.GetBytes(input);
        Byte[] hashedBytes = algorithm.ComputeHash(inputBytes);
        return BitConverter.ToString(hashedBytes).Replace("-", "");
    }

    private string YoudaoPost(string url, Dictionary<String, String> dic)
    {
        ServicePointManager.ServerCertificateValidationCallback = MyRemoteCertificateValidationCallback;

        string result = "";
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);

        req.Timeout = 20000;
        req.Headers.Add("Accept-Language", "zh-cn,en-us;q=0.5");
        //  Request.Headers.Add("Accept-Encoding", "gzip, deflate");

        req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        req.KeepAlive = true;
        req.ProtocolVersion = HttpVersion.Version11;
        req.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8";
        //Request.Accept = "text/json,*/*;q=0.5";
        //Request.Headers.Add("Accept-Charset", "utf-8;q=0.7,*;q=0.7");
        //Request.Headers.Add("Accept-Encoding", "gzip, deflate, x-gzip, identity; q=0.9");
        req.UserAgent = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/67.0.3396.87 Safari/537.36";
        //httpWebRequest.Referer = url;
        req.IfModifiedSince = DateTime.UtcNow;

        req.Method = "POST";
        req.ContentType = "application/x-www-form-urlencoded";

        StringBuilder builder = new StringBuilder();
        int i = 0;
        foreach (var item in dic)
        {
            if (i > 0)
                builder.Append("&");
            builder.AppendFormat("{0}={1}", item.Key, item.Value);
            i++;
        }
        byte[] data = Encoding.UTF8.GetBytes(builder.ToString());
        req.ContentLength = data.Length;
        using (Stream reqStream = req.GetRequestStream())
        {
            reqStream.Write(data, 0, data.Length);
            reqStream.Close();
        }
        HttpWebResponse resp = (HttpWebResponse)req.GetResponse();
        if (resp.ContentType.ToLower().Equals("audio/mp3"))
        {
            //SaveBinaryFile(resp, "合成的音频存储路径");
        }
        else
        {
            Stream stream = resp.GetResponseStream();
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                result = reader.ReadToEnd();
            }
            Console.WriteLine(result);
        }
        return result;
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
    public string EnglishName;
    public Layer Layer;
    public string SpritePath;
    public List<LayerNode> Children=new List<LayerNode>();
}
