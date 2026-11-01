namespace SoundRadar.Core.Events;

/// <summary>Тип звука для подписи маркера (классификация по профилю полос).</summary>
public enum SoundEventType
{
    /// <summary>Не распознан — подпись не рисуется.</summary>
    Unknown,

    /// <summary>Выстрел: бас-ствол плюс вч-щелчок пули, слоистый профиль.</summary>
    Shot,

    /// <summary>Шаги: энергия в средних полосах, короткое событие.</summary>
    Footsteps,

    /// <summary>Щелчок: доминируют верхние полосы.</summary>
    Click,
}
