using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GoogleMobileAds.Api;

public class GameManager : MonoBehaviour
{
    private const string PREF_PREMIUM = "premium";
    private const string PREF_SOUND = "Sound";
    private const string PREF_LANGUAGE = "Language";
    private const string PREF_SCORE = "Score";

    [Header("AUDIO")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip[] _soundFiles; // 0: Éxito, 1: Error, 2: Tictac/Alerta, 3: Clic

    [Header("LABELS")]
    [SerializeField] private TextMeshProUGUI[] _labels; 
    // 0: labelTime, 1: time, 2: labelScore, 3: score, 4: labelPath, 5: textAnswer, 6: labelColor, 7: textFollow, 8: textDeveloper, 9: labelStore

    [Header("BUTTONS & CONTROLS")]
    [SerializeField] private Button[] _buttonsColors;
    [SerializeField] private TextMeshProUGUI[] _textInButtons;
    [SerializeField] private Button _btnAds;
    [SerializeField] private Button _btnConfigToggle;
    [SerializeField] private Image _imgAvatarDisplay;
    [SerializeField] private Image _imgLanguageDisplay;
    [SerializeField] private Image _imgSoundDisplay;

    [Header("SPRITES")]
    [SerializeField] private Sprite[] imgAvatar;
    [SerializeField] private Sprite[] imgLanguages;
    [SerializeField] private Sprite[] imgPremium;
    [SerializeField] private Sprite[] imgSound;

    [Header("PANELS")]
    [SerializeField] private GameObject _panelConfig;
    [SerializeField] private GameObject _panelInfo;
    [SerializeField] private GameObject _panelUser;
    [SerializeField] private TextMeshProUGUI _textUserPrompt;
    [SerializeField] private Button[] _buttonsUser;

    private int _score = 0;
    private const int _points = 10;
    private int _time = 10;
    private const float _updateInterval = 1f;

    private string _language = "EN";
    private bool _flagEN = true;
    private bool _sound = true;
    private bool _isConfigOpen = false;

    private Coroutine _timerCoroutine;
    private Coroutine _resetCoroutine;

    private string DeveloperCredit => _language == "EN" 
        ? $"MADE BY BETWEEN BYTE SOFTWARE - {DateTime.Now.Year}" 
        : $"HECHO POR BETWEEN BYTE SOFTWARE - {DateTime.Now.Year}";

    private void Awake()
    {
        _isConfigOpen = false;

        // Auto-asignación segura para evitar UnassignedReferenceException
        ResolveMissingReferences();

        // Inicialización temprana de anuncios AdMob
        MobileAds.Initialize(_ => { });

        // Cargar ajustes guardados
        LoadSoundSetting();
        LoadLanguageSetting();
        LoadScoreSetting();
        UpdatePremiumStatus();
    }

    private void Start()
    {
        UpdateStaticLabels();

        if (_panelUser != null) _panelUser.SetActive(false);
        if (_panelConfig != null) _panelConfig.SetActive(false);
        if (_panelInfo != null) _panelInfo.SetActive(false);

        if (_imgAvatarDisplay != null && imgAvatar != null && imgAvatar.Length > 0)
        {
            _imgAvatarDisplay.sprite = imgAvatar[0];
        }

        GenerateColors();
        RestartTimer();
    }

    private void OnEnable()
    {
        InAppManager.OnPurchaseSuccess += HandleSuccessfulPurchase;
        InAppManager.OnPurchaseError += HandleFailedPurchase;
    }

    private void OnDisable()
    {
        InAppManager.OnPurchaseSuccess -= HandleSuccessfulPurchase;
        InAppManager.OnPurchaseError -= HandleFailedPurchase;
    }

    // --- RESOLUCIÓN AUTOMÁTICA DE COMPONENTES ---
    private void ResolveMissingReferences()
    {
        if (_panelInfo == null) _panelInfo = GameObject.Find("panelInfo");
        if (_panelConfig == null) _panelConfig = GameObject.Find("panelConfig");
        if (_panelUser == null) _panelUser = GameObject.Find("panelUser");

        if (_btnAds == null)
        {
            var go = GameObject.Find("btnAds");
            if (go != null) _btnAds = go.GetComponent<Button>();
        }

        if (_btnConfigToggle == null)
        {
            var go = GameObject.Find("btnOpen");
            if (go != null) _btnConfigToggle = go.GetComponent<Button>();
        }

        if (_imgAvatarDisplay == null)
        {
            var go = GameObject.Find("imgDefault");
            if (go != null) _imgAvatarDisplay = go.GetComponent<Image>();
        }

        if (_imgLanguageDisplay == null)
        {
            var go = GameObject.Find("imgLanguage");
            if (go != null) _imgLanguageDisplay = go.GetComponent<Image>();
        }

        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                var canvas = GameObject.Find("Canvas Game");
                if (canvas != null) _audioSource = canvas.GetComponent<AudioSource>();
            }
        }
    }

    // --- LÓGICA DE JUEGO ---

    public void CheckColor(int index)
    {
        if (index < 0 || index >= _buttonsColors.Length) return;

        var selectedButtonText = _textInButtons[index].text;

        if (_labels[6].text.Equals(selectedButtonText, StringComparison.OrdinalIgnoreCase))
        {
            _score += _points;
            SaveScore();
            PlaySound(_soundFiles[0]);
            if (_imgAvatarDisplay != null && imgAvatar.Length > 1) _imgAvatarDisplay.sprite = imgAvatar[1];

            if (_resetCoroutine != null) StopCoroutine(_resetCoroutine);
            _resetCoroutine = StartCoroutine(ResetDataRoutine());
        }
        else
        {
            _score = Mathf.Max(0, _score - _points);
            SaveScore();
            PlaySound(_soundFiles[1]);
            if (_imgAvatarDisplay != null && imgAvatar.Length > 2) _imgAvatarDisplay.sprite = imgAvatar[2];

            _buttonsColors[index].interactable = false;
            _textInButtons[index].color = Color.white;
        }
    }

    private void GenerateColors()
    {
        var sourceList = _language == "EN" ? Colors.newColorEN.ToList() : Colors.newColorES.ToList();
        if (sourceList.Count == 0) return;

        var random = new System.Random();
        var targetColor = sourceList[random.Next(0, sourceList.Count)];

        List<int> colorIndices = new List<int> { 1, 2, 3, 4, 5, targetColor.IdColor };
        colorIndices = colorIndices.OrderBy(_ => random.Next()).Take(_buttonsColors.Length).ToList();

        _labels[6].text = targetColor.Name;
        _labels[6].fontSize = targetColor.Name.Length < 7 ? 60 : (targetColor.Name.Length < 10 ? 50 : 40);

        for (int i = 0; i < _buttonsColors.Length; i++)
        {
            int lookupId = colorIndices[i];
            var matchedColor = sourceList.FirstOrDefault(x => x.IdColor == lookupId) ?? targetColor;

            _buttonsColors[i].GetComponent<Image>().color = ParseHexColor(matchedColor.Hex);
            _textInButtons[i].text = matchedColor.Name;
            _textInButtons[i].color = new Color(0, 0, 0, 0);
            _buttonsColors[i].interactable = true;
        }
    }

    private IEnumerator ResetDataRoutine()
    {
        foreach (var btn in _buttonsColors) btn.interactable = false;
        foreach (var txt in _textInButtons) txt.color = Color.black;

        yield return new WaitForSeconds(1f);

        GenerateColors();
        _labels[1].color = Color.white;
        if (_imgAvatarDisplay != null && imgAvatar.Length > 0) _imgAvatarDisplay.sprite = imgAvatar[0];

        RestartTimer();
    }

    private void RestartTimer()
    {
        if (_timerCoroutine != null) StopCoroutine(_timerCoroutine);
        _time = 10;
        _labels[1].text = _time.ToString();
        _labels[1].color = Color.white;
        _timerCoroutine = StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        float nextTick = Time.time + _updateInterval;

        while (_time > 0)
        {
            yield return null;

            if (Time.time >= nextTick)
            {
                _time--;
                _labels[1].text = _time.ToString();

                if (_time == 5)
                {
                    _labels[1].color = Color.red;
                    PlaySound(_soundFiles[2]);
                }
                nextTick += _updateInterval;
            }
        }

        if (_resetCoroutine != null) StopCoroutine(_resetCoroutine);
        _resetCoroutine = StartCoroutine(ResetDataRoutine());
    }

    // --- IAP & PREMIUM ---

    public void BuyPremium()
    {
        PlayClick();

        if (InAppManager.Instance != null && InAppManager.Instance.HasPurchasedNonConsumable(PREF_PREMIUM))
        {
            OpenPanel("panelInfo");
            if (_labels.Length > 9) _labels[9].text = _language == "EN" ? "YOU ARE ALREADY PREMIUM" : "YA ERES PREMIUM";
            SetPremiumVisuals(true);
            return;
        }

        if (InAppManager.Instance != null)
        {
            InAppManager.Instance.BuyNonConsumable(PREF_PREMIUM);
        }
    }

    private void HandleSuccessfulPurchase(string productId)
    {
        if (productId != PREF_PREMIUM) return;

        LocalStorage.SaveData(PREF_PREMIUM, PREF_PREMIUM);
        SetPremiumVisuals(true);
        OpenPanel("panelInfo");
        if (_labels.Length > 9) _labels[9].text = _language == "EN" ? "THANKS FOR YOUR PURCHASE" : "GRACIAS POR TU COMPRA";
    }

    private void HandleFailedPurchase(string productId)
    {
        OpenPanel("panelInfo");
        if (_labels.Length > 9) _labels[9].text = _language == "EN" ? "PURCHASE FAILED" : "COMPRA FALLIDA";
    }

    private void UpdatePremiumStatus()
    {
        bool isPremium = LocalStorage.LoadKey(PREF_PREMIUM) || (InAppManager.Instance != null && InAppManager.Instance.HasPurchasedNonConsumable(PREF_PREMIUM));
        SetPremiumVisuals(isPremium);

        if (!isPremium)
        {
            StartCoroutine(PromptAdsRoutine());
        }
    }

    private void SetPremiumVisuals(bool isPremium)
    {
        if (_btnAds == null) return;

        if (isPremium)
        {
            LocalStorage.SaveData(PREF_PREMIUM, PREF_PREMIUM);
            if (imgPremium != null && imgPremium.Length > 1) _btnAds.GetComponent<Image>().sprite = imgPremium[1];
            _btnAds.GetComponent<RectTransform>().sizeDelta = new Vector2(40, 40);
        }
        else
        {
            if (imgPremium != null && imgPremium.Length > 0) _btnAds.GetComponent<Image>().sprite = imgPremium[0];
        }
    }

    // --- ADS ---

    private IEnumerator PromptAdsRoutine()
    {
        yield return new WaitForSeconds(10f);

        if (LocalStorage.LoadKey(PREF_PREMIUM)) yield break;

        if (_textUserPrompt != null)
            _textUserPrompt.text = _language == "EN" ? "Would you like to watch a video?" : "¿Deseas ver un video?";

        if (_buttonsUser != null && _buttonsUser.Length >= 2)
        {
            _buttonsUser[0].GetComponentInChildren<TMP_Text>().text = _language == "EN" ? "YES" : "SI";
            _buttonsUser[1].GetComponentInChildren<TMP_Text>().text = "NO";
        }

        if (_panelUser != null) _panelUser.SetActive(true);
    }

    public void ShowAds(bool userConsent)
    {
        PlayClick();
        if (_panelUser != null) _panelUser.SetActive(false);

        if (userConsent)
        {
            AdsBanner.Instance?.LoadAdsBanner();
            AdsRewarded.Instance?.LoadAdsRewarded();
        }
    }

    // --- IDIOMA Y CONFIGURACIÓN ---

    public void ChangeLanguage()
    {
        PlayClick();
        _flagEN = !_flagEN;
        _language = _flagEN ? "EN" : "ES";

        LocalStorage.SaveData(PREF_LANGUAGE, _language);
        if (_imgLanguageDisplay != null && imgLanguages != null && imgLanguages.Length > 1)
        {
            _imgLanguageDisplay.sprite = _flagEN ? imgLanguages[1] : imgLanguages[0];
        }

        UpdateStaticLabels();
        GenerateColors();
        ClosePanel("panelConfig");
    }

    public void ChangeSound()
    {
        PlayClick();
        _sound = !_sound;
        if (_audioSource != null) _audioSource.mute = !_sound;

        LocalStorage.SaveData(PREF_SOUND, _sound ? "ON" : "OFF");
        if (_imgSoundDisplay != null && imgSound != null && imgSound.Length > 1)
        {
            _imgSoundDisplay.sprite = _sound ? imgSound[1] : imgSound[0];
        }

        ClosePanel("panelConfig");
    }

    private void UpdateStaticLabels()
    {
        if (_labels == null || _labels.Length < 9) return;

        _labels[0].text = _language == "EN" ? "TIME" : "TIEMPO";
        _labels[2].text = _language == "EN" ? "SCORE" : "PUNTOS";
        _labels[4].text = _language == "EN" ? "DATABASE: OK" : "BASE DE DATOS: OK";
        _labels[5].text = _language == "EN" ? "WHAT COLOR IS" : "QUE COLOR ES";
        _labels[7].text = _language == "EN" ? "FOLLOW US" : "SIGUENOS";
        _labels[8].text = DeveloperCredit;
    }

    private void LoadSoundSetting()
    {
        _sound = !LocalStorage.LoadKey(PREF_SOUND) || LocalStorage.LoadData(PREF_SOUND) == "ON";
        if (_audioSource != null) _audioSource.mute = !_sound;
        if (_imgSoundDisplay != null && imgSound != null && imgSound.Length > 1)
        {
            _imgSoundDisplay.sprite = _sound ? imgSound[1] : imgSound[0];
        }
    }

    private void LoadLanguageSetting()
    {
        _language = LocalStorage.LoadKey(PREF_LANGUAGE) ? LocalStorage.LoadData(PREF_LANGUAGE) : "EN";
        _flagEN = _language == "EN";
        if (_imgLanguageDisplay != null && imgLanguages != null && imgLanguages.Length > 1)
        {
            _imgLanguageDisplay.sprite = _flagEN ? imgLanguages[1] : imgLanguages[0];
        }
    }

    private void LoadScoreSetting()
    {
        _score = LocalStorage.LoadKey(PREF_SCORE) ? int.Parse(LocalStorage.LoadData(PREF_SCORE)) : 0;
        if (_labels != null && _labels.Length > 3 && _labels[3] != null)
        {
            _labels[3].text = _score.ToString();
        }
    }

    private void SaveScore()
    {
        LocalStorage.SaveData(PREF_SCORE, _score.ToString());
        if (_labels != null && _labels.Length > 3 && _labels[3] != null)
        {
            _labels[3].text = _score.ToString();
        }
    }

    // --- PANELES Y NAVEGACIÓN UI ---

    public void OpenPanel(string panelName)
    {
        if (panelName == "panelConfig" && _panelConfig != null)
        {
            _isConfigOpen = !_isConfigOpen;
            _panelConfig.SetActive(_isConfigOpen);
            RotateConfigButton(_isConfigOpen);
            PlayClick();
        }
        else if (panelName == "panelInfo" && _panelInfo != null)
        {
            _panelInfo.SetActive(true);
        }
    }

    public void ClosePanel(string panelName)
    {
        PlayClick();
        if (panelName == "panelConfig" && _panelConfig != null)
        {
            _isConfigOpen = false;
            _panelConfig.SetActive(false);
            RotateConfigButton(false);
        }
        else if (panelName == "panelInfo" && _panelInfo != null)
        {
            _panelInfo.SetActive(false);
        }
    }

    private void RotateConfigButton(bool active)
    {
        if (_btnConfigToggle != null)
        {
            _btnConfigToggle.transform.localRotation = Quaternion.Euler(0, 0, active ? -40f : 0f);
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (_sound && clip != null && _audioSource != null)
        {
            _audioSource.clip = clip;
            _audioSource.Play();
        }
    }

    private void PlayClick()
    {
        if (_sound && _audioSource != null && _soundFiles != null && _soundFiles.Length > 3 && _soundFiles[3] != null)
        {
            _audioSource.PlayOneShot(_soundFiles[3], 0.5f);
        }
    }

    private Color ParseHexColor(string hex)
    {
        return ColorUtility.TryParseHtmlString(hex, out Color parsedColor) ? parsedColor : Color.white;
    }

    public void LauncherURL(string url) => Application.OpenURL(url);
}