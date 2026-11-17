namespace RevGame;

/// <summary>ฟิลเตอร์ 1 โพล ใช้ได้ทั้ง low-pass และ high-pass</summary>
internal sealed class OnePole
{
    private float _a = 1f;
    private float _z;

    public void SetCutoff(float hz, float sampleRate)
    {
        _a = 1f - MathF.Exp(-2f * MathF.PI * hz / sampleRate);
    }

    public float LowPass(float x)
    {
        _z += _a * (x - _z);
        return _z;
    }

    public float HighPass(float x)
    {
        _z += _a * (x - _z);
        return x - _z;
    }
}
