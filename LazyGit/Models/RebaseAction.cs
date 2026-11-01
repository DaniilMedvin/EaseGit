namespace LazyGit.Models;

/// <summary>Что interactive rebase делает с коммитом.</summary>
public enum RebaseAction
{
    /// <summary>Оставить как есть.</summary>
    Pick,

    /// <summary>Оставить изменения, сменить заголовок сообщения.</summary>
    Reword,

    /// <summary>Остановиться после коммита, чтобы его поправить (amend), затем продолжить.</summary>
    Edit,

    /// <summary>Влить в предыдущий (более старый) коммит, сообщения объединяются.</summary>
    Squash,

    /// <summary>Влить в предыдущий коммит, сообщение этого коммита отбрасывается.</summary>
    Fixup,

    /// <summary>Убрать коммит из истории.</summary>
    Drop,
}
