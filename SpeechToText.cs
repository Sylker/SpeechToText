using System;
using System.IO;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using System.Collections;
using UnityEngine.Networking;

public class SpeechToText : MonoBehaviour
{
    // Configuration variables set via Unity Inspector
    [SerializeField] private string googleApiKey = "YOUR_GOOGLE_CLOUD_API_KEY";

    // Minimum amount of time (in seconds) the user must speak before silence can trigger automatic stop.
    // Prevents false triggers from short sounds or accidental activation.
    [SerializeField] private float minRecordingDuration = 0.5f;

    // Threshold of average microphone volume used to detect silence.
    // Lower values make the system more sensitive to quiet sounds.
    [SerializeField] private float silenceThreshold = 0.01f;

    // Duration (in seconds) of continuous silence required to trigger stop and transcription.
    [SerializeField] private float silenceDuration = 0.5f;

    // Internal timer to track how long the user has been silent.
    private float silenceTimer = 0f;

    // Stores the audio recorded from the microphone.
    private AudioClip recordedClip;

    // Timer to measure the total duration of the current recording session.
    private float recordingTimer = 0f;

    // Sample rate (Hz) used when recording audio from the microphone.
    // Must match the format expected by Google STT API (e.g., 16000 Hz).
    private const int sampleRate = 16000;

    // Indicates whether the system is currently listening for voice input.
    private bool isListening = false;

    // Events for stopping UI feedback and handling transcription
    public event Action OnStopListening;
    public event Action<string> OnTranscript;

    private void Start() { /* Reserved for initialization if needed */ }

    void Update()
    {
        if (!isListening || !Microphone.IsRecording(null)) return;

        recordingTimer += Time.deltaTime;

        // Sample recent microphone data to detect silence
        float[] samples = new float[256];
        int micPos = Microphone.GetPosition(null) - samples.Length;
        if (micPos < 0) return;
        recordedClip.GetData(samples, micPos);

        float avgVolume = 0f;
        foreach (float s in samples) avgVolume += Mathf.Abs(s);
        avgVolume /= samples.Length;

        // Trigger stop if silence exceeds threshold
        if (avgVolume < silenceThreshold && recordingTimer > minRecordingDuration)
        {
            silenceTimer += Time.deltaTime;
            if (silenceTimer > silenceDuration)
            {
                Debug.Log("Silence detected. Stopping...");
                StopRecordingAndTranscribe();
            }
        }
        else
        {
            silenceTimer = 0f;
        }
    }

    // Starts short recording with manual stop
    public void StartRecording()
    {
        recordedClip = Microphone.Start(null, false, 5, sampleRate);
        Debug.Log("Recording started...");
    }

    // Stops microphone and starts transcription coroutine
    public void StopRecordingAndTranscribe()
    {
        OnStopListening?.Invoke();
        isListening = false;
        Microphone.End(null);
        Debug.Log("Recording stopped.");
        StartCoroutine(ProcessAndSendAudio());
    }

    // Starts voice activity detection recording
    public void StartListening()
    {
        recordedClip = Microphone.Start(null, true, 10, sampleRate);
        isListening = true;
        silenceTimer = 0f;
        recordingTimer = 0f;
        Debug.Log("Listening...");
    }

    // Converts recorded audio to base64 and sends it to Google STT
    IEnumerator ProcessAndSendAudio()
    {
        Debug.Log("Converting to WAV...");
        byte[] wavData = AudioClipToWAV(recordedClip);

        string base64Audio = Convert.ToBase64String(wavData);
        var requestData = new
        {
            config = new
            {
                encoding = "LINEAR16",
                sampleRateHertz = sampleRate,
                languageCode = "pt-BR"
            },
            audio = new
            {
                content = base64Audio
            }
        };

        string json = JsonConvert.SerializeObject(requestData);
        using (UnityWebRequest request = new UnityWebRequest(
            $"https://speech.googleapis.com/v1/speech:recognize?key={googleApiKey}", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("Google STT Response: " + request.downloadHandler.text);

                STTResponse stt = JsonConvert.DeserializeObject<STTResponse>(request.downloadHandler.text);
                if (stt.results != null && stt.results.Length > 0)
                {
                    string transcript = stt.results[0].alternatives[0].transcript;
                    Debug.Log("Transcript: " + transcript);
                    OnTranscript?.Invoke(transcript);
                }
            }
            else
            {
                Debug.LogError("STT Error: " + request.error);
                Debug.LogError("STT Response: " + request.downloadHandler.text);
            }
        }
    }

    // Converts an AudioClip to WAV byte array format
    byte[] AudioClipToWAV(AudioClip clip)
    {
        float[] samples = new float[clip.samples];
        clip.GetData(samples, 0);
        byte[] wav = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short sample = (short)(samples[i] * short.MaxValue);
            byte[] bytes = BitConverter.GetBytes(sample);
            wav[i * 2] = bytes[0];
            wav[i * 2 + 1] = bytes[1];
        }

        using (MemoryStream ms = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(ms))
        {
            int fileSize = 44 + wav.Length;
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(fileSize - 8);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(wav.Length);
            writer.Write(wav);
            return ms.ToArray();
        }
    }

    // Helper classes for deserializing Google STT response
    [Serializable]
    public class STTResponse { public STTResult[] results; }
    [Serializable]
    public class STTResult { public STTAlternative[] alternatives; }
    [Serializable]
    public class STTAlternative { public string transcript; public float confidence; }
}