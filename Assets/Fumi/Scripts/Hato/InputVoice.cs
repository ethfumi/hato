﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class InputVoice : SingletonMonoBehaviour<InputVoice>
{
    public class NoteNameDetector
    {
        private string[] noteNames = { "ド", "ド♯", "レ", "レ♯", "ミ", "ファ", "ファ♯", "ソ", "ソ♯", "ラ", "ラ♯", "シ" };

        public string GetNoteName(float freq)
        {
            if (freq == 0)
                return "";

            // 周波数からMIDIノートナンバーを計算
            var noteNumber = calculateNoteNumberFromFrequency(freq);
            // 0:C - 11:B に収める
            var note = noteNumber % 12;
            // 0:C～11:Bに該当する音名を返す
            return noteNames[note];
        }

        // See https://en.wikipedia.org/wiki/MIDI_tuning_standard
        private int calculateNoteNumberFromFrequency(float freq)
        {
            return Mathf.FloorToInt(69 + 12 * Mathf.Log(freq / 440, 2));
        }
    }

    public int LowFreq  = 150;
    public int HighFreq = 800;
    public float ThresholdVolume = 1;
    public float PrevEffectRate = 0;

    [SerializeField] Text debugText;
    [SerializeField] InputPowerView view;
    [SerializeField] Player player;

    AudioSource audioSource;

    void Start()
    {
        StartCoroutine(InputStart());
    }

    // https://qiita.com/niusounds/items/b8858a2b043676185a54
    // 基音をとるのはこのへん
    // http://ibako-study.hateblo.jp/entry/2014/02/06/031945
    IEnumerator InputStart()
    {
        audioSource = GetComponent<AudioSource>();

        audioSource.clip = Microphone.Start(null, true, 10, 44100);  // マイク名、ループするかどうか、AudioClipの秒数、サンプリングレート を指定する
        audioSource.loop = true;
        while (!(Microphone.GetPosition("") > 0)){ yield return null; }             // マイクが取れるまで待つ。空文字でデフォルトのマイクを探してくれる
        audioSource.Play();                                           // 再生する

        const int windowSize = 1024; // 解像度高くしたかったので256から1024に変更
        float[] spectrum = new float[windowSize];

        float prevVolume = 0;
        var prevFreq = 0;
        float threshold = 0.04f * audioSource.volume; //ピッチとして検出する最小の分布

        while (true)
        {
            audioSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);

            var maxIndex = 0;
            var maxValue = 0.0f;
            for (int i = 0; i < spectrum.Length; i++)
            {
                var val = spectrum[i];
                if (val > maxValue && val > threshold)
                {
                    maxValue = val;
                    maxIndex = i;
                }
            }

            float freqN = maxIndex;
            if (maxIndex > 0 && maxIndex < spectrum.Length - 1)
            {
                //隣のスペクトルも考慮する
                float dL = spectrum[maxIndex - 1] / spectrum[maxIndex];
                float dR = spectrum[maxIndex + 1] / spectrum[maxIndex];
                freqN += 0.5f * (dR * dR - dL * dL);
            }

            var freq = (int)(freqN * AudioSettings.outputSampleRate / 2 / spectrum.Length);
            maxValue = maxValue / audioSource.volume < 0.0513f ? 0 : maxValue; // 無音のときにも0.0512が入る
            var volume = maxValue / audioSource.volume;
            volume = Mathf.Lerp(volume, prevVolume, PrevEffectRate);
            freq = (int)Mathf.Lerp(freq, prevFreq, PrevEffectRate);

            debugText.text = string.Format("周波数{0} volume{1:###0.0000}", freq, volume);

            var rate = (volume / audioSource.volume) < ThresholdVolume ? 0 : Mathf.InverseLerp(LowFreq, HighFreq, freq); // 小さい音を無視
            view.SetPower(rate);
            player.Boost(rate);

            prevVolume = volume;
            prevFreq = freq;

            yield return null;
        }
    }
}