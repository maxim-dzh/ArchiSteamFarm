# 📋 Руководство по работе с Git Submodules в ArchiSteamFarm

## 🎯 Проблема которую мы решаем

Когда вы делаете изменения в submodule (например, ASFEnhance) и затем обновляете основной репозиторий, ваши изменения могут сброситься. Это руководство поможет избежать этой проблемы.

## 🔧 Основные правила работы с submodules

### 1. ❌ Что НЕ делать

```bash
# ❌ НИКОГДА не делайте это если у вас есть локальные изменения:
git submodule update --init --recursive

# ❌ Это тоже сбросит ваши изменения:
git submodule update --force
```

### 2. ✅ Что делать ПРАВИЛЬНО

#### Перед любыми операциями с submodules:

```bash
# 1. Проверьте статус всех submodules
git submodule foreach git status

# 2. Если есть изменения - сохраните их
cd ASFEnhance
git add .
git commit -m "Мои локальные изменения"

# 3. Или создайте stash
git stash push -m "Временные изменения"
```

## 🚀 Стратегии сохранения изменений

### Стратегия 1: Локальная ветка (рекомендуется)

```bash
# В submodule создайте локальную ветку
cd ASFEnhance
git checkout -b my-custom-changes

# Делайте свои изменения и коммитьте
git add .
git commit -m "Мои кастомные изменения для интеграции с основным проектом"

# При обновлении submodule:
git fetch origin
git checkout main  # переключаемся на оригинальную ветку
git pull origin main  # обновляем

# Затем применяем свои изменения:
git checkout my-custom-changes
git rebase main  # или git merge main
```

### Стратегия 2: Stash (для временных изменений)

```bash
cd ASFEnhance

# Сохраняем изменения
git stash push -m "Мои изменения для проекта"

# Обновляем
git pull origin main

# Восстанавливаем изменения
git stash pop
```

### Стратегия 3: Patch файлы (для переносимости)

```bash
cd ASFEnhance

# Создаем patch с вашими изменениями
git diff > ../my-asfenhance-changes.patch

# После обновления submodule:
git apply ../my-asfenhance-changes.patch
```

## 🔄 Правильные команды синхронизации

### Безопасное обновление основного проекта:

```bash
# 1. Используйте наш продвинутый скрипт
./sync-upstream-advanced.sh

# 2. Или делайте вручную:
git fetch upstream
git merge upstream/main

# 3. Для submodules используйте:
./sync-upstream-advanced.sh --no-submodules  # пропустить submodules
```

### Безопасное обновление конкретного submodule:

```bash
cd ASFEnhance

# Проверяем статус
git status

# Если есть изменения - сохраняем
git stash push -m "Local changes $(date)"

# Обновляем
git fetch origin
git checkout main
git pull origin main

# Восстанавливаем изменения
git stash pop
```

## 📋 Ежедневный Workflow

### 1. Начало работы с проектом:

```bash
# Клонируем проект с submodules
git clone --recursive https://github.com/YourName/ArchiSteamFarm.git

cd ArchiSteamFarm

# Настраиваем upstream
git remote add upstream https://github.com/JustArchiNET/ArchiSteamFarm.git

# Создаем локальные ветки в submodules для своих изменений
cd ASFEnhance
git checkout -b my-integration-changes
cd ..
```

### 2. Работа с изменениями:

```bash
# Делаем изменения в ASFEnhance
cd ASFEnhance
# ... редактируем файлы ...
git add .
git commit -m "Update paths to work with main project"

cd ..

# Собираем и тестируем
dotnet build
```

### 3. Синхронизация с upstream:

```bash
# Используем наш умный скрипт
./sync-upstream-advanced.sh

# Или вручную:
# 1. Основной проект
git fetch upstream
git merge upstream/main

# 2. Submodules (осторожно!)
cd ASFEnhance
git stash  # сохраняем изменения
git fetch origin
git checkout main
git pull origin main
git checkout my-integration-changes
git rebase main  # применяем изменения поверх новой версии
```

## 🛠️ Полезные команды

### Проверка состояния:

```bash
# Статус всех submodules
git submodule foreach git status

# Список веток в submodule
cd ASFEnhance && git branch -a

# Показать все stash'и
cd ASFEnhance && git stash list

# Показать изменения в submodule
git diff --submodule
```

### Восстановление:

```bash
# Восстановить stash
cd ASFEnhance && git stash pop

# Переключиться на локальную ветку
cd ASFEnhance && git checkout my-integration-changes

# Принудительно сбросить submodule (ОСТОРОЖНО!)
git submodule update --force ASFEnhance
```

### Работа с ветками:

```bash
# Создать ветку для изменений
cd ASFEnhance
git checkout -b feature/my-changes

# Переключиться между ветками
git checkout main                    # оригинальная версия
git checkout feature/my-changes      # ваши изменения

# Слить изменения
git checkout feature/my-changes
git merge main  # или git rebase main
```

## 🚨 Аварийное восстановление

Если вы случайно потеряли изменения:

```bash
# 1. Проверьте reflog
cd ASFEnhance
git reflog

# 2. Найдите нужный коммит и восстановите
git checkout -b recovery-branch <commit-hash>

# 3. Проверьте stash
git stash list
git stash show stash@{0}
git stash pop stash@{0}

# 4. Поищите в backup ветках
git branch -a | grep local-changes
git checkout local-changes-20241220-143022
```

## 📚 Примеры команд для разных ситуаций

### Ситуация 1: Хочу обновить только основной проект

```bash
./sync-upstream-advanced.sh --no-submodules
```

### Ситуация 2: Хочу обновить все, но у меня есть изменения

```bash
# Предварительно сохраните изменения
cd ASFEnhance
git add .
git commit -m "My local changes"

# Затем обновляйте
cd ..
./sync-upstream-advanced.sh
```

### Ситуация 3: Хочу начать заново с чистого submodule

```bash
cd ASFEnhance
git stash  # сохранить изменения на всякий случай
git checkout main
git reset --hard origin/main
```

### Ситуация 4: Хочу применить свои изменения к новой версии

```bash
cd ASFEnhance
git checkout my-integration-changes
git rebase main  # применить изменения поверх обновленной версии
```

## 💡 Лучшие практики

1. **Всегда создавайте локальную ветку** для своих изменений в submodule
2. **Регулярно коммитьте** свои изменения
3. **Используйте описательные имена** для веток и коммитов
4. **Тестируйте сборку** после каждого обновления
5. **Делайте backup** важных изменений
6. **Используйте наш умный скрипт** для синхронизации

## 🎯 Заключение

Следуя этим правилам, вы никогда не потеряете свои изменения в submodules и сможете безопасно обновляться до новых версий ArchiSteamFarm! 