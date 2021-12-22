using Microsoft.International.Converters.PinYinConverter;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class PSD2UGUIEditor : EditorWindow
{
    [MenuItem("Tools/PSD2UGUIEditor")]
    static void Main()
    {
        GetWindow<PSD2UGUIEditor>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private string _inputText;
    private void OnGUI()
    {
        _inputText = EditorGUILayout.TextField(_inputText);
        if (GUILayout.Button("OK"))
        {
            if (!string.IsNullOrEmpty(_inputText))
            {
                StringBuilder stringBuilder = new StringBuilder();
                foreach (var item in _inputText)
                {
                    ChineseChar cc = new ChineseChar(item);
                    stringBuilder.Append(cc.Pinyins.ToList()[0]);
                }
                Debug.Log(stringBuilder.ToString());
            }
        }
    }
}
