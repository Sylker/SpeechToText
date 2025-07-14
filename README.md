# SpeechToText

This script captures microphone input in Unity and uses voice activity detection (VAD) to automatically detect when the user stops speaking. 
Once silence is detected, it sends the recorded audio to the Google Cloud Speech-to-Text API for transcription. 
The resulting text can then be used for further interaction, such as triggering responses from a text-to-speech or conversational AI system.
