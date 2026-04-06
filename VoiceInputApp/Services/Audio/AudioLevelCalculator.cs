namespace VoiceInputApp.Services.Audio;

public class AudioLevelCalculator
{
    private float _currentLevel;
    private const float AttackCoeff = 0.4f;
    private const float ReleaseCoeff = 0.15f;

    public float CurrentLevel => _currentLevel;

    public float CalculateLevel(byte[] buffer, int bytesRecorded)
    {
        var rms = CalculateRms(buffer, bytesRecorded);
        var targetLevel = Math.Clamp(rms * 3f, 0f, 1f);

        if (targetLevel > _currentLevel)
        {
            _currentLevel += (targetLevel - _currentLevel) * AttackCoeff;
        }
        else
        {
            _currentLevel += (targetLevel - _currentLevel) * ReleaseCoeff;
        }

        return _currentLevel;
    }

    private static float CalculateRms(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2) return 0;

        long sum = 0;
        var sampleCount = bytesRecorded / 2;

        for (var i = 0; i < bytesRecorded - 1; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sum += sample * sample;
        }

        var meanSquare = (double)sum / sampleCount;
        var rms = Math.Sqrt(meanSquare) / 32768.0;
        return (float)rms;
    }
}
