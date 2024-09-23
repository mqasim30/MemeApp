using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Collections.Generic;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using System.IO;
using System;

public class MemeAppController : MonoBehaviour
{
    [Header("UI Elements")]
    [Tooltip("The initial logo or loading screen to display.")]
    public GameObject logoScreen;
    
    [Tooltip("ScrollRect component for scrolling through memes.")]
    public ScrollRect scrollRect;
    
    [Tooltip("Content panel inside the ScrollRect where images will be instantiated.")]
    public RectTransform contentPanel;

    [Header("Image Settings")]
    [Tooltip("Prefab for individual meme images.")]
    public RawImage imagePrefab;
    
    [Tooltip("List to store fetched meme image URLs.")]
    public List<string> memeUrls = new List<string>();
    
    [Tooltip("Number of images to load initially.")]
    public int initialLoadCount = 5;
    
    [Tooltip("Number of images to load in each subsequent buffer.")]
    public int bufferSize = 5;
    
    private int loadedImageCount = 0; // Tracks the number of images currently loaded
    private bool isLoadingImages = false; // Prevents multiple simultaneous image load operations

    [Header("Google Sheets Settings")]
    [Tooltip("Your Google Sheets ID.")]
    public string spreadsheetId;
    
    [Tooltip("Initial range to fetch 100 URLs (e.g., Sheet1!A1:A100).")]
    public string range = "Sheet1!A1:A100";
    
    [Tooltip("Path to the Google API credentials JSON file (placed in StreamingAssets).")]
    public string credentialsFilePath;
    
    private SheetsService service; // Google Sheets service reference
    private int nextUrlFetchThreshold = 90; // Threshold to fetch new URLs
    private bool isFetchingUrls = false; // Prevents multiple simultaneous URL fetch operations

    private void Awake()
    {
        // Validate essential components
        if (logoScreen == null)
            Debug.LogError("LogoScreen is not assigned.");
        if (scrollRect == null)
            Debug.LogError("ScrollRect is not assigned.");
        if (contentPanel == null)
            Debug.LogError("ContentPanel is not assigned.");
        if (imagePrefab == null)
            Debug.LogError("ImagePrefab is not assigned.");
        if (string.IsNullOrEmpty(spreadsheetId))
            Debug.LogError("SpreadsheetId is not set.");
        if (string.IsNullOrEmpty(credentialsFilePath))
            Debug.LogError("CredentialsFilePath is not set.");
    }

    private IEnumerator Start()
    {
        Debug.Log($"[{DateTime.Now}] Starting app... Showing logo screen.");
        logoScreen.SetActive(true);

        // Initialize Google Sheets service
        yield return StartCoroutine(InitializeSheetsService());

        if (service == null)
        {
            Debug.LogError("Google Sheets service failed to initialize. Aborting.");
            yield break;
        }

        // List all sheet names for verification
        yield return StartCoroutine(ListAllSheetNames());

        // Fetch initial URLs
        yield return StartCoroutine(FetchUrlsFromGoogleSheets());

        // Begin loading images
        yield return StartCoroutine(InitialLoadCoroutine());
    }

    /// <summary>
    /// Initializes the Google Sheets service using credentials.
    /// </summary>
    private IEnumerator InitializeSheetsService()
    {
        Debug.Log($"[{DateTime.Now}] Initializing Google Sheets service...");
        string credentialsPath = Path.Combine(Application.streamingAssetsPath, credentialsFilePath);

        if (!File.Exists(credentialsPath))
        {
            Debug.LogError($"Credentials file not found at path: {credentialsPath}");
            yield break;
        }

        bool initializationCompleted = false;
        bool initializationSuccess = false;
        GoogleCredential loadedCredential = null;

        // Start the initialization process
        StartCoroutine(LoadGoogleCredentials(credentialsPath, (success, credential) =>
        {
            initializationSuccess = success;
            loadedCredential = credential;
            initializationCompleted = true;
        }));

        // Wait until initialization is completed
        while (!initializationCompleted)
        {
            yield return null;
        }

        if (initializationSuccess && loadedCredential != null)
        {
            // Initialize the SheetsService
            service = new SheetsService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = loadedCredential,
                ApplicationName = "MemeApp",
            });

            Debug.Log($"[{DateTime.Now}] Google Sheets service initialized successfully.");
        }
        else
        {
            Debug.LogError("Failed to load Google credentials. Service will not be initialized.");
        }
    }

    /// <summary>
    /// Coroutine to load Google credentials without using try-catch.
    /// </summary>
    /// <param name="path">Path to the credentials JSON file.</param>
    /// <param name="callback">Callback to return the result.</param>
    private IEnumerator LoadGoogleCredentials(string path, Action<bool, GoogleCredential> callback)
    {
        GoogleCredential credential = null;
        bool loadSuccess = false;

        // Attempt to load the credentials
        // Since FileStream operations are synchronous, we perform them inside a try-catch outside yield
        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleCredential.FromStream(stream)
                    .CreateScoped(SheetsService.Scope.SpreadsheetsReadonly);
            }
            loadSuccess = true;
            Debug.Log($"[{DateTime.Now}] Google credentials loaded successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading Google credentials: {ex.Message}");
            loadSuccess = false;
            credential = null;
        }

        // Invoke the callback with the result
        callback?.Invoke(loadSuccess, credential);

        yield return null;
    }

    /// <summary>
    /// Coroutine to list all sheet names in the spreadsheet for verification.
    /// </summary>
    private IEnumerator ListAllSheetNames()
    {
        if (service == null)
        {
            Debug.LogError("Google Sheets service is not initialized.");
            yield break;
        }

        // Fetch the spreadsheet metadata
        var request = service.Spreadsheets.Get(spreadsheetId);
        var fetchTask = request.ExecuteAsync();

        // Wait until the task is completed
        while (!fetchTask.IsCompleted)
        {
            yield return null;
        }

        if (fetchTask.IsFaulted || fetchTask.IsCanceled)
        {
            Debug.LogError($"Error fetching spreadsheet metadata: {fetchTask.Exception?.Message}");
        }
        else
        {
            var spreadsheet = fetchTask.Result;
            foreach (var sheet in spreadsheet.Sheets)
            {
                Debug.Log($"Sheet Name: {sheet.Properties.Title}");
            }
        }

        yield return null;
    }

    /// <summary>
    /// Coroutine for the initial image loading process.
    /// </summary>
    private IEnumerator InitialLoadCoroutine()
    {
        Debug.Log($"[{DateTime.Now}] Loading initial batch of images...");
        yield return StartCoroutine(LoadImagesCoroutine(initialLoadCount));

        Debug.Log($"[{DateTime.Now}] Initial images loaded. Hiding logo screen.");
        logoScreen.SetActive(false);
        scrollRect.enabled = true;

        // Update the threshold based on the current memeUrls count
        nextUrlFetchThreshold = Mathf.FloorToInt(memeUrls.Count * 0.9f);

        // Load next buffer if necessary
        if (loadedImageCount < memeUrls.Count)
        {
            LoadNextBuffer();
        }

        Debug.Log($"[{DateTime.Now}] Monitoring scroll for additional image loading...");
    }

    private void Update()
    {
        // Determine if more images need to be loaded based on scroll position
        if (!isLoadingImages && loadedImageCount < memeUrls.Count)
        {
            float dynamicThreshold = CalculateDynamicThreshold(loadedImageCount);

            bool shouldLoadMore = false;
            if (scrollRect.horizontal)
            {
                shouldLoadMore = scrollRect.horizontalNormalizedPosition >= dynamicThreshold;
            }
            else
            {
                shouldLoadMore = scrollRect.verticalNormalizedPosition <= (1 - dynamicThreshold);
            }

            if (shouldLoadMore)
            {
                Debug.Log($"[{DateTime.Now}] Scroll threshold reached ({dynamicThreshold * 100:F1}%). Loading next buffer of images...");
                LoadNextBuffer();
            }
        }

        // Fetch more URLs when 90% of the current URLs have been loaded
        if (!isFetchingUrls && loadedImageCount >= nextUrlFetchThreshold)
        {
            Debug.Log($"[{DateTime.Now}] Loaded 90% of current URLs. Fetching next batch...");
            StartCoroutine(FetchMoreUrls());
        }
    }

    /// <summary>
    /// Initiates loading the next buffer of images.
    /// </summary>
    private void LoadNextBuffer()
    {
        StartCoroutine(LoadNextBufferCoroutine());
    }

    /// <summary>
    /// Coroutine to load the next set of images.
    /// </summary>
    private IEnumerator LoadNextBufferCoroutine()
    {
        if (isLoadingImages)
            yield break;

        isLoadingImages = true;

        int imagesToLoad = Mathf.Min(bufferSize, memeUrls.Count - loadedImageCount);
        if (imagesToLoad <= 0)
        {
            isLoadingImages = false;
            yield break;
        }

        yield return StartCoroutine(LoadImagesCoroutine(imagesToLoad));

        isLoadingImages = false;
    }

    /// <summary>
    /// Coroutine to load a specified number of images.
    /// </summary>
    /// <param name="count">Number of images to load.</param>
    private IEnumerator LoadImagesCoroutine(int count)
    {
        List<UnityWebRequest> requests = new List<UnityWebRequest>();
        List<RawImage> newImages = new List<RawImage>();

        for (int i = 0; i < count; i++)
        {
            if (loadedImageCount >= memeUrls.Count)
                break;

            string url = memeUrls[loadedImageCount];
            RawImage newImage = Instantiate(imagePrefab, contentPanel);
            newImage.gameObject.SetActive(false); // Hide until loaded
            newImages.Add(newImage);
            loadedImageCount++;

            Debug.Log($"[{DateTime.Now}] Queuing download for image {loadedImageCount}/{memeUrls.Count} from URL: {url}");

            UnityWebRequest request = UnityWebRequestTexture.GetTexture(url);
            requests.Add(request);
            // Removed the following line to prevent sending the request here
            // request.SendWebRequest();
        }

        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < requests.Count; i++)
        {
            UnityWebRequest request = requests[i];
            // Send the request and wait for it to complete
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError($"[{DateTime.Now}] Error downloading image from {request.url}: {request.error}");
                // Optionally, handle retries or provide a placeholder
                Destroy(newImages[i].gameObject);
            }
            else
            {
                Texture2D texture = DownloadHandlerTexture.GetContent(request);
                if (texture != null)
                {
                    newImages[i].texture = texture;
                    newImages[i].gameObject.SetActive(true); // Show the image once loaded
                    Debug.Log($"[{DateTime.Now}] Successfully downloaded image from {request.url}.");
                }
                else
                {
                    Debug.LogError($"[{DateTime.Now}] Received null texture from {request.url}.");
                    Destroy(newImages[i].gameObject);
                }
            }
        }

        stopwatch.Stop();
        Debug.Log($"[{DateTime.Now}] Loaded {count} images in {stopwatch.ElapsedMilliseconds} ms.");
    }

    /// <summary>
    /// Coroutine to fetch more meme URLs from Google Sheets.
    /// </summary>
    private IEnumerator FetchMoreUrls()
    {
        isFetchingUrls = true;

        yield return StartCoroutine(FetchUrlsFromGoogleSheets());

        // Update the threshold based on the new number of URLs
        nextUrlFetchThreshold = Mathf.FloorToInt(memeUrls.Count * 0.9f);

        isFetchingUrls = false;
    }

    /// <summary>
    /// Coroutine to fetch URLs from Google Sheets and add them to the memeUrls list.
    /// </summary>
    private IEnumerator FetchUrlsFromGoogleSheets()
    {
        Debug.Log($"[{DateTime.Now}] Fetching URLs from Google Sheets...");

        if (service == null)
        {
            Debug.LogError("Google Sheets service is not initialized.");
            yield break;
        }

        bool fetchCompleted = false;
        bool fetchSuccess = false;
        ValueRange fetchedData = null;

        // Start the fetch operation
        StartCoroutine(FetchUrlsFromGoogleSheetsCoroutine((success, response) =>
        {
            fetchSuccess = success;
            if (success && response != null)
            {
                fetchedData = response;
            }
            fetchCompleted = true;
        }));

        // Wait until fetch is completed
        while (!fetchCompleted)
        {
            yield return null;
        }

        if (fetchSuccess && fetchedData != null)
        {
            ProcessFetchedUrls(fetchedData);
            Debug.Log($"[{DateTime.Now}] Finished fetching URLs from Google Sheets.");
        }
        else
        {
            Debug.LogError($"[{DateTime.Now}] Failed to fetch URLs from Google Sheets.");
        }
    }

    /// <summary>
    /// Coroutine to fetch URLs from Google Sheets without using try-catch.
    /// </summary>
    /// <param name="callback">Callback to return the result.</param>
    private IEnumerator FetchUrlsFromGoogleSheetsCoroutine(Action<bool, ValueRange> callback)
    {
        var request = service.Spreadsheets.Values.Get(spreadsheetId, range);
        var fetchTask = request.ExecuteAsync();

        // Wait until the task is completed
        while (!fetchTask.IsCompleted)
        {
            yield return null;
        }

        if (fetchTask.IsFaulted || fetchTask.IsCanceled)
        {
            Debug.LogError($"Error fetching URLs: {fetchTask.Exception?.Message}");
            callback?.Invoke(false, null);
        }
        else
        {
            ValueRange response = fetchTask.Result;
            callback?.Invoke(true, response);
        }
    }

    /// <summary>
    /// Processes the fetched URLs and adds them to the memeUrls list.
    /// </summary>
    /// <param name="response">The response containing the URLs.</param>
    private void ProcessFetchedUrls(ValueRange response)
    {
        IList<IList<object>> values = response.Values;

        if (values != null && values.Count > 0)
        {
            int addedCount = 0;
            foreach (var row in values)
            {
                if (row.Count > 0)
                {
                    string url = row[0].ToString().Trim();
                    if (IsValidUrl(url))
                    {
                        memeUrls.Add(url);
                        addedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"Invalid URL skipped: {url}");
                    }
                }
            }

            Debug.Log($"[{DateTime.Now}] Successfully fetched {addedCount} URLs from Google Sheets.");
        }
        else
        {
            Debug.LogWarning($"[{DateTime.Now}] The range '{range}' is empty or does not exist.");
        }

        // Update the range for the next batch of URLs
        int nextStartRow = memeUrls.Count + 1;
        range = $"Sheet1!A{nextStartRow}:A{nextStartRow + 99}";
    }

    /// <summary>
    /// Validates whether a string is a well-formed URL.
    /// </summary>
    /// <param name="url">URL string to validate.</param>
    /// <returns>True if valid, otherwise false.</returns>
    private bool IsValidUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult) &&
               (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
    }

    /// <summary>
    /// Calculates a dynamic threshold for loading more images based on the number of loaded images.
    /// </summary>
    /// <param name="loadedCount">Number of images currently loaded.</param>
    /// <returns>Threshold value between 0 and 1.</returns>
    private float CalculateDynamicThreshold(int loadedCount)
    {
        if (loadedCount <= 0)
            return 0.6f;

        // Dynamic threshold increases logarithmically with the number of loaded images
        float threshold = 0.6f + 0.1f * Mathf.Log(loadedCount + 1);

        // Clamp the threshold between 0.6 and 0.95
        threshold = Mathf.Clamp(threshold, 0.6f, 0.95f);

        return threshold;
    }
}
