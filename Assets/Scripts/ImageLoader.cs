using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class ImageLoader : MonoBehaviour
{
    public string imageUrl = "https://img-9gag-fun.9cache.com/photo/aND1DX6_700bwp.png"; // WebP image URL
    public RawImage targetRawImage; // Reference to the UI RawImage component

    IEnumerator Start()
    {
        // Download the WebP image
        UnityWebRequest webRequest = UnityWebRequestTexture.GetTexture(imageUrl);
        yield return webRequest.SendWebRequest();

        if (webRequest.result == UnityWebRequest.Result.ConnectionError || webRequest.result == UnityWebRequest.Result.ProtocolError)
        {
            Debug.LogError(webRequest.error);
        }
        else
        {
            // Get the downloaded texture
            Texture2D webPTexture = ((DownloadHandlerTexture)webRequest.downloadHandler).texture;
            
            // Assign the texture to the RawImage component
            targetRawImage.texture = webPTexture;

            // Optionally, you could convert the texture to PNG and save it locally
            //byte[] pngData = webPTexture.EncodeToPNG();
            //System.IO.File.WriteAllBytes(Application.persistentDataPath + "/downloadedImage.png", pngData);
            Debug.Log("Image saved as PNG to " + Application.persistentDataPath);
        }
    }
}
