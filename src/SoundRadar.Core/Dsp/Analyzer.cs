namespace SoundRadar.Core.Dsp;

using SoundRadar.Core.Events;

/// <summary>
/// Конвейер анализа: блоки 1024 отсчётов → FFT по каждому каналу → 5 полос → адаптивный фон →
/// онсет-детектор → направление.
///
/// Направление:
///  - стерео (2 канала) — азимут из ILD по constant-power pan law, −90…+90°, зад не различим;
///  - 4/6/8 каналов (quad/5.1/7.1) — векторная сумма энергий по углам динамиков, −180…+180°,
///    перед и зад различимы: мы видим задний сектор в многоканальном миксе игры ДО того,
///    как Windows сведёт его в стерео для наушников;
///  - моно — 0°, прочее число каналов — сводится парами чётные/нечётные и считается как стерео.
///
/// Энергия нормирована так, что синус полного масштаба в полосе даёт E = A² (0 дБ).
/// </summary>
public sealed class Analyzer
{
    private const int BlockSize = AnalyzerOptions.BlockSize;
    private const int PendingCapacity = BlockSize * 4;
    private const int MaxChannels = 8;

    private readonly AnalyzerOptions opt;
    private readonly EventHub hub;
    private readonly Fft fft = new(BlockSize);
    private readonly double[] window = MakeHann(BlockSize);
    private readonly float[][] pending = new float[MaxChannels][];
    private readonly double[][] bufRe = new double[MaxChannels][];
    private readonly double[][] bufIm = new double[MaxChannels][];
    private readonly object gate = new();

    private int pendingCount;
    private int pendingPos;

    private int sampleRate;
    private int channels;
    private ChannelMap? map;
    private BandSplitter? splitter;
    private BackgroundTracker[]? backgrounds;
    private int[] aboveCount = Array.Empty<int>();
    private bool[] bandEvent = Array.Empty<bool>();
    private double[][] channelBandEnergy = Array.Empty<double[]>();
    private double dtMs;

    private SoundEvent? current;
    private SoundEvent? lastEnded;
    private double lastEndedAudioSec = -1e9;
    private double quietMs;
    private double[] frozenMs = Array.Empty<double>();
    // Окно усреднения азимута: только блоки в пределах 3 дБ от пика события — хвост и реверб
    // не тянут угол к центру, а одно-блочный шум не решает направление.
    private double livePeakDb = -999;
    private double liveAzSin;
    private double liveAzCos;

    private long samplesSeen;

    public Analyzer(EventHub hub, AnalyzerOptions? options = null)
    {
        this.hub = hub;
        opt = options ?? new AnalyzerOptions();
        for (var c = 0; c < MaxChannels; c++)
        {
            pending[c] = new float[PendingCapacity];
            bufRe[c] = new double[BlockSize];
            bufIm[c] = new double[BlockSize];
        }
    }

    public AnalyzerOptions Options => opt;

    public bool IsEventActive => current is not null;

    /// <summary>
    /// Принять captured interleaved float32. Число каналов фиксируется при первом чанке;
    /// смена формата на ходу переинициализирует анализатор (с прогревом).
    /// </summary>
    public void ProcessSamples(float[] interleaved, int ch, int rate)
    {
        if (ch <= 0 || ch > MaxChannels || interleaved.Length % ch != 0)
            throw new ArgumentException(
                $"Некорректный чанк: {ch} каналов, длина {interleaved.Length}.", nameof(interleaved));

        lock (gate)
        {
            if (rate != sampleRate || ch != channels)
                Initialize(rate, ch);

            var frames = interleaved.Length / ch;
            for (var f = 0; f < frames; f++)
            {
                var baseIdx = ch * f;
                for (var c = 0; c < ch; c++)
                    pending[c][(pendingPos + pendingCount) % PendingCapacity] = interleaved[baseIdx + c];
                if (pendingCount < PendingCapacity)
                    pendingCount++;
                else
                    pendingPos = (pendingPos + 1) % PendingCapacity; // анализатор не успевает — роняем самый старый кадр
            }

            while (pendingCount >= BlockSize)
                ProcessBlock();
        }
    }

    public void Reset()
    {
        lock (gate)
        {
            pendingCount = 0;
            pendingPos = 0;
            samplesSeen = 0;
            Array.Clear(frozenMs);
            current = null;
            lastEnded = null;
            quietMs = 0;
            Array.Clear(aboveCount);
            Array.Clear(bandEvent);
            if (backgrounds is not null)
                foreach (var bg in backgrounds)
                    bg.Reset();
        }
    }

    private void Initialize(int rate, int ch)
    {
        sampleRate = rate;
        channels = ch;
        samplesSeen = 0;
        map = ChannelMap.For(ch);
        splitter = new BandSplitter(AnalyzerOptions.BandEdgesHz, BlockSize, rate);
        backgrounds = new BackgroundTracker[splitter.BandCount];
        for (var b = 0; b < splitter.BandCount; b++)
            backgrounds[b] = new BackgroundTracker(opt.TauRiseSec, opt.TauFallSec, opt.AbsFloorDb);
        aboveCount = new int[splitter.BandCount];
        bandEvent = new bool[splitter.BandCount];
        frozenMs = new double[splitter.BandCount];
        channelBandEnergy = new double[ch][];
        for (var c = 0; c < ch; c++)
            channelBandEnergy[c] = new double[splitter.BandCount];
        dtMs = 1000.0 * BlockSize / rate;
    }

    private void ProcessBlock()
    {
        var bands = splitter!.BandCount;
        var floor = Math.Pow(10, opt.AbsFloorDb / 10.0);
        var scale = 16.0 / (BlockSize * (double)BlockSize);

        for (var i = 0; i < BlockSize; i++)
        {
            var idx = (pendingPos + i) % PendingCapacity;
            for (var c = 0; c < channels; c++)
            {
                bufRe[c][i] = pending[c][idx] * window[i];
                bufIm[c][i] = 0;
            }
        }
        pendingPos = (pendingPos + BlockSize) % PendingCapacity;
        pendingCount -= BlockSize;
        samplesSeen += BlockSize;
        // Аудио-время: все решения о таймингах событий — по нему, не по настенным часам
        // (в оффлайн-реплее аудио течёт быстрее реального времени).
        var audioSec = samplesSeen / (double)sampleRate;

        for (var c = 0; c < channels; c++)
            fft.Forward(bufRe[c], bufIm[c]);

        var exceed = new double[bands];
        var bgDb = new double[bands];
        var bandTotal = new double[bands];
        for (var b = 0; b < bands; b++)
        {
            double total = 0;
            for (var c = 0; c < channels; c++)
            {
                double s = 0;
                for (var k = splitter.BinStart(b); k < splitter.BinEnd(b); k++)
                    s += bufRe[c][k] * bufRe[c][k] + bufIm[c][k] * bufIm[c][k];
                channelBandEnergy[c][b] = s * scale;
                total += s * scale;
            }
            bandTotal[b] = total;

            var level = Math.Max(backgrounds![b].Level, floor);
            exceed[b] = 10.0 * Math.Log10(Math.Max(total, 1e-30) / level);
            bgDb[b] = 10.0 * Math.Log10(level);

            if (current is null)
            {
                // Онсет: превышение порога MinBlocks блоков подряд. Во время прогрева фон ещё
                // сходится — онсеты подавлены, иначе первые секунды дают шквал ложных событий.
                // Поправка по полосам: тихие, но характерные звуки (вч-щелчок пули) детектируются охотнее.
                if (samplesSeen >= opt.WarmupSec * sampleRate)
                {
                    if (exceed[b] > opt.ThresholdDb - BandThresholdBoost(b))
                        aboveCount[b]++;
                    else
                        aboveCount[b] = 0;
                    if (aboveCount[b] >= opt.MinBlocks)
                        bandEvent[b] = true;
                }
            }
            else
            {
                bandEvent[b] = exceed[b] > opt.ReleaseDb;
            }
        }

        var utc = DateTime.UtcNow;
        var maxExceed = 0.0;
        foreach (var t in exceed)
            if (t > maxExceed)
                maxExceed = t;

        if (current is null)
        {
            var anyOnset = false;
            foreach (var active in bandEvent)
                anyOnset |= active;

            if (anyOnset)
            {
                // Короткая пауза (< MergeGapMs аудио-времени) не рвёт событие — возобновляем последнее,
                // но если оно уже отработало свой лимит длительности — начинаем новое (иначе
                // птичий треск будет вечно resurrection'ить одно и то же событие).
                var resume = lastEnded is not null
                    && audioSec - lastEndedAudioSec <= opt.MergeGapMs / 1000.0
                    && (lastEnded.AudioEndSec!.Value - lastEnded.AudioStartSec) * 1000.0 < opt.EventMaxDurMs;
                var e = resume ? lastEnded! : new SoundEvent { StartedUtc = utc, AudioStartSec = audioSec };
                if (resume)
                {
                    // Возобновлённое событие снова живо: сбрасываем флаг конца, иначе
                    // потребители (оверлей) считают его завершённым и игнорируют обновления.
                    e.EndedUtc = null;
                }
                else
                {
                    hub.RaiseStarted(e);
                }
                current = e;
                lastEnded = null;
                quietMs = 0;
                livePeakDb = -999;
                liveAzSin = 0;
                liveAzCos = 0;
            }
        }

        // Направление считается только по блокам с честной громкостью события (уровень онсета).
        // Порог затухания (4 дБ) здесь нельзя: фоновые колебания замороженного фона ±3–5 дБ
        // пересчитывали бы азимут по шумовому полю всех каналов и утаскивали его к центроиду,
        // а RearSeen заражался бы тихим шумом задних каналов.
        if (current is not null && maxExceed > opt.ThresholdDb)
        {
            ComputeDirection(exceed, out var az, out var rearShare);
            if (rearShare > 0.05)
                current.RearSeen = true;
            current.FrontalOnly = !current.RearSeen;

            // Азимут якорится к пику события: усредняются только блоки в пределах 3 дБ от
            // пика. Выстрел слоистый (ствол + центрированный щелчок пули + реверб-хвост) —
            // усреднение по всему событию тянуло угол к центру.
            if (maxExceed > livePeakDb + 3.0)
            {
                livePeakDb = maxExceed;
                liveAzSin = 0;
                liveAzCos = 0;
            }
            else if (maxExceed > livePeakDb)
            {
                livePeakDb = maxExceed;
            }

            if (maxExceed >= livePeakDb - 3.0)
            {
                // Вес блока растёт с близостью к пику: «рампа» онсета (разбавленная окном Ханна
                // половина блока) почти не влияет, полные блоки решают.
                var w = Math.Max(0.0, maxExceed - (livePeakDb - 3.0));
                if (w > 0)
                {
                    var th = az * Math.PI / 180.0;
                    liveAzSin += w * Math.Sin(th);
                    liveAzCos += w * Math.Cos(th);
                    current.AzimuthDeg = NormalizeAngle(Math.Atan2(liveAzSin, liveAzCos) * 180.0 / Math.PI);
                }
            }
        }

        if (current is not null)
        {
            if (maxExceed > current.SalienceDb)
                current.SalienceDb = maxExceed;
            current.Confidence = Math.Clamp((current.SalienceDb - opt.MinSalienceDb) / 18.0, 0.0, 1.0);
            for (var b = 0; b < bands; b++)
                current.BandSalienceDb[b] = Math.Max(current.BandSalienceDb[b], exceed[b]);

            // Событие затихло, когда почти все полосы ушли ниже порога затухания: одна шумная
            // полоса (птица в вч, порыв ветра в басах) не должна держать событие открытым вечно.
            var activeBands = 0;
            foreach (var t in exceed)
                if (t > opt.ReleaseDb)
                    activeBands++;
            quietMs = activeBands > bands - opt.QuietBandsRequired ? 0 : quietMs + dtMs;

            var eventDurMs = (audioSec - current.AudioStartSec) * 1000.0;
            if (quietMs >= opt.ReleaseMs || eventDurMs >= opt.EventMaxDurMs)
            {
                current.EndedUtc = utc;
                current.AudioEndSec = audioSec;
                var ended = current;
                current = null;
                lastEnded = ended;
                lastEndedAudioSec = audioSec;
                Array.Clear(bandEvent);
                Array.Clear(aboveCount);
                hub.RaiseEnded(ended);
            }
        }

        // Фон: полосы, участвующие в событии, не обновляются (иначе событие задерёт свой же порог).
        // Во время прогрева фон сходится быстро, чтобы тихий вход не давал ложных превышений.
        // Защита от latch (холодный старт сразу с громкого звука): если полоса заморожена
        // дольше 1.5 с непрерывно — фон застрял, принудительно учим его (быстрый τ), пока
        // превышение не упадёт. Иначе событие никогда не закончится.
        var warming = samplesSeen < opt.WarmupSec * sampleRate;
        for (var b = 0; b < bands; b++)
        {
            var frozen = current is not null && (exceed[b] > opt.ReleaseDb || bandEvent[b]);
            frozenMs[b] = frozen ? frozenMs[b] + dtMs : 0.0;
            var forceLearn = frozenMs[b] > 1500.0;
            backgrounds![b].Update(bandTotal[b], dtMs / 1000.0, frozen && !forceLearn, fast: warming || forceLearn);
        }

        PublishMeter(bands, exceed, bgDb);
    }

    /// <summary>
    /// Направление на источник по текущему блоку: азимут (−180…+180, 0 = перед, минус = слева)
    /// и доля энергии задних каналов (для честной пометки «зад не различим»).
    /// </summary>
    private void ComputeDirection(double[] exceed, out double az, out double rearShare)
    {
        var bands = splitter!.BandCount;
        rearShare = 0;

        switch (channels)
        {
            case 2:
            {
                // Стерео: ILD каждой полосы → калиброванная кривая панорамы игры → взвешенное
                // среднее углов. Вес полосы = превышение над фоном × вес полосы направления
                // (у DayZ направленный компонент — басовый ствол, вч-щелчок пули почти по центру).
                double angleSum = 0, weightSum = 0;
                for (var b = 0; b < bands; b++)
                {
                    var w = Math.Max(0.0, exceed[b] - 3.0) * BandDirectionWeight(b);
                    if (w <= 0)
                        continue;
                    var ild = 10.0 * Math.Log10((channelBandEnergy[1][b] + 1e-30) / (channelBandEnergy[0][b] + 1e-30));
                    angleSum += w * MapIldToAngle(ild);
                    weightSum += w;
                }
                az = weightSum > 0 ? angleSum / weightSum : 0;
                return;
            }
            case 1:
            {
                az = 0;
                return;
            }
        }

        if (map!.SupportsDirection360)
        {
            // Векторная сумма энергий по углам динамиков → полный круг.
            double vecX = 0, vecY = 0, eFront = 0, eRear = 0;
            for (var c = 0; c < channels; c++)
            {
                var angle = map.AnglesDeg[c];
                if (double.IsNaN(angle))
                    continue; // LFE и прочее без направления
                var e = WeightedEnergy(c, exceed);
                if (e <= 0)
                    continue;
                var th = angle * Math.PI / 180.0;
                vecX += e * Math.Sin(th);
                vecY += e * Math.Cos(th);
                if (Math.Abs(angle) > 90.0)
                    eRear += e;
                else
                    eFront += e;
            }
            az = Math.Atan2(vecX, vecY) * 180.0 / Math.PI;
            rearShare = eRear / Math.Max(1e-30, eRear + eFront);
            return;
        }

        // Экзотическое число каналов: сводим парами чётные/нечётные и считаем как стерео.
        double el = 0, er = 0;
        for (var c = 0; c < channels; c++)
        {
            if ((c & 1) == 0)
                el += WeightedEnergy(c, exceed);
            else
                er += WeightedEnergy(c, exceed);
        }
        az = PanMath.AzimuthFromEnergies(el, er);
    }

    /// <summary>Энергия канала по полосам события; вес полосы = превышение над фоном − 3 дБ
    /// × вес полосы направления (самый громкий компонент события тянет угол на себя).</summary>
    private double WeightedEnergy(int channel, double[] exceed)
    {
        var bands = splitter!.BandCount;
        double sum = 0;
        for (var b = 0; b < bands; b++)
        {
            var w = Math.Max(0.0, exceed[b] - 3.0) * BandDirectionWeight(b);
            sum += w * channelBandEnergy[channel][b];
        }
        return sum;
    }

    private double BandDirectionWeight(int band) =>
        band < opt.BandDirectionWeights.Length ? opt.BandDirectionWeights[band] : 1.0;

    private double BandThresholdBoost(int band) =>
        band < opt.BandThresholdBoostDb.Length ? opt.BandThresholdBoostDb[band] : 0.0;

    /// <summary>
    /// ILD (10·log10(E_R/E_L)) → азимут −90…+90. Без калибровки — обратная constant-power
    /// панорама; с калибровкой — кусочно-линейная интерполяция таблицы игры с экстраполяцией
    /// по краям (зажим ±90).
    /// </summary>
    private double MapIldToAngle(double ildDb)
    {
        var cal = opt.IldCalibration;
        if (cal is null || cal.Length == 0)
        {
            var p = 4.0 / Math.PI * Math.Atan(Math.Sqrt(Math.Pow(10, ildDb / 10.0))) - 1.0;
            return 90.0 * Math.Clamp(p, -1.0, 1.0);
        }

        var pts = cal.OrderBy(p => p.IldDb).ToArray();
        if (pts.Length == 1)
            return ildDb >= pts[0].IldDb ? pts[0].AngleDeg : 0;

        double Map((double IldDb, double AngleDeg) a, (double IldDb, double AngleDeg) b, double x) =>
            a.AngleDeg + (b.AngleDeg - a.AngleDeg) * (x - a.IldDb) / (b.IldDb - a.IldDb);

        double angle;
        if (ildDb <= pts[0].IldDb)
            angle = Map(pts[0], pts[1], Math.Max(ildDb, pts[0].IldDb - (pts[1].IldDb - pts[0].IldDb)));
        else if (ildDb >= pts[^1].IldDb)
            angle = Map(pts[^2], pts[^1], Math.Min(ildDb, pts[^1].IldDb + (pts[^1].IldDb - pts[^2].IldDb)));
        else
        {
            var i = 0;
            while (i < pts.Length - 2 && ildDb > pts[i + 1].IldDb)
                i++;
            angle = Map(pts[i], pts[i + 1], ildDb);
        }
        return Math.Clamp(angle, -90.0, 90.0);
    }

    private void PublishMeter(int bands, double[] exceed, double[] bgDb)
    {
        var meterL = new double[bands];
        var meterR = new double[bands];
        for (var b = 0; b < bands; b++)
        {
            switch (channels)
            {
                case 1:
                    meterL[b] = meterR[b] = channelBandEnergy[0][b] / 2;
                    break;
                case 2:
                    meterL[b] = channelBandEnergy[0][b];
                    meterR[b] = channelBandEnergy[1][b];
                    break;
                default:
                    if (map is { SupportsDirection360: true })
                    {
                        for (var c = 0; c < channels; c++)
                        {
                            var angle = map.AnglesDeg[c];
                            if (double.IsNaN(angle))
                                continue;
                            if (angle < 0) meterL[b] += channelBandEnergy[c][b];
                            else if (angle > 0) meterR[b] += channelBandEnergy[c][b];
                            else { meterL[b] += channelBandEnergy[c][b] / 2; meterR[b] += channelBandEnergy[c][b] / 2; }
                        }
                    }
                    else
                    {
                        for (var c = 0; c < channels; c++)
                        {
                            if ((c & 1) == 0) meterL[b] += channelBandEnergy[c][b];
                            else meterR[b] += channelBandEnergy[c][b];
                        }
                    }
                    break;
            }
        }

        double eFront = 0, eRear = 0;
        if (map is { HasRear: true })
        {
            for (var c = 0; c < channels; c++)
            {
                var angle = map.AnglesDeg[c];
                if (double.IsNaN(angle))
                    continue;
                double e = 0;
                for (var b = 0; b < bands; b++)
                    e += channelBandEnergy[c][b];
                if (Math.Abs(angle) > 90.0)
                    eRear += e;
                else
                    eFront += e;
            }
        }

        hub.PublishMeter(new MeterFrame(
            meterL, meterR, exceed, bgDb, sampleRate, DateTime.UtcNow, current is not null,
            channels, map is { HasRear: true },
            10.0 * Math.Log10(Math.Max(eRear, 1e-30) / Math.Max(eFront, 1e-30))));
    }

    private static double NormalizeAngle(double a)
    {
        a %= 360.0;
        if (a > 180.0)
            a -= 360.0;
        if (a < -180.0)
            a += 360.0;
        return a;
    }

    private static double[] MakeHann(int n)
    {
        var w = new double[n];
        for (var i = 0; i < n; i++)
            w[i] = 0.5 * (1.0 - Math.Cos(2 * Math.PI * i / n)); // периодическое окно
        return w;
    }
}
