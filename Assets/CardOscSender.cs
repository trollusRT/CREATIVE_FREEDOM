using UnityEngine;
using UnityEngine.EventSystems;
using extOSC;

public enum CardColor
{
    Purple,
    Red,
    Orange,
    Yellow,
    Green,
    Blue
}

public class CardOscSender : MonoBehaviour, IPointerEnterHandler
{
    [Header("Card Data")]
    public CardColor color;
    public int cost = 1; // 1 or 2

    [Header("OSC")]
    public OSCTransmitter transmitter;
    public string oscAddress = "/card/hover";

    public void OnPointerEnter(PointerEventData eventData)
    {
        // Fire a short note pulse when hovered
        StartCoroutine(SendNotePulse());
    }

    private System.Collections.IEnumerator SendNotePulse()
    {
        SendNote(gate: 1);
        yield return new WaitForSeconds(0.1f); // short pulse
        SendNote(gate: 0);
    }

    private void SendNote(int gate)
    {
        int rootMidi = GetRootMidi(color);
        int[] chord = BuildChord(rootMidi, cost);

        var msg = new OSCMessage(oscAddress);
        msg.AddValue(OSCValue.Float(MidiToVOct(chord[0])));
        msg.AddValue(OSCValue.Float(MidiToVOct(chord[1])));
        msg.AddValue(OSCValue.Float(MidiToVOct(chord[2])));
        msg.AddValue(OSCValue.Int(cost));
        msg.AddValue(OSCValue.Int(gate));

        transmitter.Send(msg);
    }

    private int GetRootMidi(CardColor c)
    {
        switch (c)
        {
            case CardColor.Purple: return 60; // C4
            case CardColor.Red: return 62; // D4
            case CardColor.Orange: return 64; // E4
            case CardColor.Yellow: return 67; // G4
            case CardColor.Green: return 69; // A4
            case CardColor.Blue: return 71; // B4
            default: return 60;
        }
    }

    // For 1-cost: return just the root; for 2-cost: major triad
    private int[] BuildChord(int rootMidi, int cost)
    {
        if (cost <= 1)
            return new[] { rootMidi, rootMidi, rootMidi }; // ignore extra voices

        // Major triad: root, +4 semitones, +7
        return new[] { rootMidi, rootMidi + 4, rootMidi + 7 };
    }

    private float MidiToVOct(int midiNote)
    {
        return (midiNote - 60) / 12f;
    }
}
