window.speechRecognitionService = {
    recognition: null,

    start: function (dotNetHelper, lang) {
        // Támogatás ellenőrzése (Chrome/Edge használja a webkit prefixet)
        const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!SpeechRecognition) {
            alert("A böngésződ nem támogatja a hangfelismerést.");
            dotNetHelper.invokeMethodAsync('RecognitionEnded');
            return;
        }

        this.recognition = new SpeechRecognition();
        this.recognition.lang = lang || 'hu-HU'; // Alapértelmezett nyelv a magyar
        this.recognition.interimResults = false; // Csak a végleges szöveget kérjük
        this.recognition.maxAlternatives = 1;

        // Amikor sikeresen felismert egy szöveget
        this.recognition.onresult = function (event) {
            const transcript = event.results[0][0].transcript;

            // Visszaküldjük a Blazor C# kódnak a szöveget
            dotNetHelper.invokeMethodAsync('ReceiveTranscript', transcript);
        };

        // Hiba esetén
        this.recognition.onerror = function (event) {
            console.error("Hangfelismerési hiba: ", event.error);
            dotNetHelper.invokeMethodAsync('ReceiveError', event.error);
        };

        // Amikor a beszéd vagy a felismerés befejeződik
        this.recognition.onend = function () {
            dotNetHelper.invokeMethodAsync('RecognitionEnded');
        };

        // Felvétel indítása
        this.recognition.start();
    },

    stop: function () {
        if (this.recognition) {
            this.recognition.stop();
        }
    }
};