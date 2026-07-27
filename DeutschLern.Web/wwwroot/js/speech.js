window.deutschLernSpeech = {
    speak(text) {
        if (!text || !("speechSynthesis" in window)) {
            return false;
        }

        window.speechSynthesis.cancel();

        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = "de-DE";
        utterance.rate = 0.85;

        const germanVoice = window.speechSynthesis
            .getVoices()
            .find(voice => voice.lang.toLowerCase().startsWith("de"));

        if (germanVoice) {
            utterance.voice = germanVoice;
        }

        window.speechSynthesis.speak(utterance);
        return true;
    }
};
