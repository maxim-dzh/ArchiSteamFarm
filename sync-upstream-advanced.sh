#!/bin/bash

# Цвета для вывода
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_status() { echo -e "${BLUE}[INFO]${NC} $1"; }
print_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
print_warning() { echo -e "${YELLOW}[WARNING]${NC} $1"; }
print_error() { echo -e "${RED}[ERROR]${NC} $1"; }

# Настройки
UPSTREAM_URL="https://github.com/JustArchiNET/ArchiSteamFarm.git"
MAIN_BRANCH="main"

# Параметры
FORCE_SYNC=false
SKIP_REBASE=false
DRY_RUN=false
UPDATE_SUBMODULES=true
FEATURE_BRANCH="feature-1"

# Парсинг аргументов
while [[ $# -gt 0 ]]; do
    case $1 in
        -f|--force) FORCE_SYNC=true; shift ;;
        -s|--skip-rebase) SKIP_REBASE=true; shift ;;
        -d|--dry-run) DRY_RUN=true; shift ;;
        -n|--no-submodules) UPDATE_SUBMODULES=false; shift ;;
        -b|--branch) FEATURE_BRANCH="$2"; shift 2 ;;
        -h|--help)
            echo "Использование: $0 [OPTIONS]"
            echo ""
            echo "Опции:"
            echo "  -f, --force           Принудительная синхронизация"
            echo "  -s, --skip-rebase     Пропустить rebase feature ветки"
            echo "  -d, --dry-run         Показать что будет сделано"
            echo "  -n, --no-submodules   Не обновлять submodules"
            echo "  -b, --branch BRANCH   Указать feature ветку для rebase"
            echo "  -h, --help            Показать справку"
            exit 0
            ;;
        *) print_error "Неизвестный параметр: $1"; exit 1 ;;
    esac
done

print_status "🚀 Начинаем умную синхронизацию ArchiSteamFarm + плагинов..."

# Проверяем git репозиторий
if ! git rev-parse --git-dir > /dev/null 2>&1; then
    print_error "Не Git репозиторий!"
    exit 1
fi

CURRENT_BRANCH=$(git branch --show-current)
print_status "Текущая ветка: $CURRENT_BRANCH"

# Проверяем несохраненные изменения
if ! git diff-index --quiet HEAD --; then
    print_warning "У вас есть несохраненные изменения!"
    if [ "$DRY_RUN" = false ]; then
        read -p "Сохранить в stash? (y/n): " -n 1 -r
        echo
        if [[ $REPLY =~ ^[Yy]$ ]]; then
            git stash push -m "Auto stash before sync $(date)"
            print_success "Изменения сохранены в stash"
        else
            print_error "Прервано пользователем"
            exit 1
        fi
    fi
fi

# Настройка upstream remote
if ! git remote get-url upstream > /dev/null 2>&1; then
    print_status "Добавляем upstream remote..."
    if [ "$DRY_RUN" = false ]; then
        git remote add upstream "$UPSTREAM_URL"
    fi
fi

# Синхронизация основного репозитория
print_status "📥 Синхронизируем ArchiSteamFarm..."
if [ "$DRY_RUN" = false ]; then
    git fetch upstream
    
    if [ "$CURRENT_BRANCH" != "$MAIN_BRANCH" ]; then
        git checkout "$MAIN_BRANCH"
    fi
    
    if [ "$FORCE_SYNC" = true ]; then
        git reset --hard "upstream/$MAIN_BRANCH"
        git push origin "$MAIN_BRANCH" --force-with-lease
    else
        git merge "upstream/$MAIN_BRANCH" --no-edit
        git push origin "$MAIN_BRANCH"
    fi
fi
print_success "ArchiSteamFarm синхронизирован"

# Умное управление submodules с сохранением изменений
manage_submodules_smart() {
    if [ "$UPDATE_SUBMODULES" = false ]; then
        print_status "Пропускаем обновление submodules"
        return
    fi
    
    print_status "🔧 Умное обновление submodules..."
    
    # Получаем список всех submodules
    git submodule foreach --quiet 'echo $name' | while read submodule_name; do
        if [ -z "$submodule_name" ]; then
            continue
        fi
        
        print_status "Обрабатываем submodule: $submodule_name"
        
        if [ -d "$submodule_name" ]; then
            cd "$submodule_name"
            
            # Проверяем есть ли локальные изменения
            if ! git diff-index --quiet HEAD --; then
                print_warning "В $submodule_name есть несохраненные изменения!"
                print_status "Сохраняем изменения в stash..."
                git stash push -m "Auto stash before submodule update $(date)"
            fi
            
            # Проверяем есть ли локальные коммиты
            UPSTREAM_COMMITS=$(git rev-list --count HEAD..origin/HEAD 2>/dev/null || echo "0")
            LOCAL_COMMITS=$(git rev-list --count origin/HEAD..HEAD 2>/dev/null || echo "0")
            
            if [ "$LOCAL_COMMITS" -gt 0 ]; then
                print_warning "В $submodule_name есть $LOCAL_COMMITS локальных коммитов!"
                print_status "Создаем ветку для сохранения изменений..."
                
                # Создаем ветку с локальными изменениями
                BACKUP_BRANCH="local-changes-$(date +%Y%m%d-%H%M%S)"
                git checkout -b "$BACKUP_BRANCH"
                print_success "Локальные изменения сохранены в ветку: $BACKUP_BRANCH"
                
                # Возвращаемся на основную ветку
                git checkout main || git checkout master
            fi
            
            # Обновляем submodule
            print_status "Обновляем $submodule_name..."
            git fetch origin
            git reset --hard origin/HEAD
            
            # Восстанавливаем stash если был
            if git stash list | grep -q "Auto stash before submodule update"; then
                print_status "Восстанавливаем локальные изменения..."
                if git stash pop; then
                    print_success "Локальные изменения восстановлены"
                else
                    print_warning "Конфликт при восстановлении изменений! Проверьте вручную."
                fi
            fi
            
            cd ..
        fi
    done
    
    # Обновляем ссылки на submodules в основном репозитории
    if [ "$DRY_RUN" = false ]; then
        git submodule update --init --recursive
    fi
}

# Вызываем умное управление submodules
if [ "$DRY_RUN" = false ]; then
    manage_submodules_smart
fi

# Ребейзим feature ветку
if [ -n "$FEATURE_BRANCH" ] && [ "$SKIP_REBASE" = false ]; then
    if git show-ref --verify --quiet "refs/heads/$FEATURE_BRANCH"; then
        print_status "🔄 Ребейзим feature ветку: $FEATURE_BRANCH"
        if [ "$DRY_RUN" = false ]; then
            git checkout "$FEATURE_BRANCH"
            if ! git rebase "$MAIN_BRANCH"; then
                print_error "Конфликт при rebase!"
                exit 1
            fi
        fi
        print_success "Feature ветка перебазирована"
    fi
elif [ "$CURRENT_BRANCH" != "$MAIN_BRANCH" ] && [ "$SKIP_REBASE" = false ]; then
    print_status "🔄 Ребейзим текущую ветку: $CURRENT_BRANCH"
    if [ "$DRY_RUN" = false ]; then
        git checkout "$CURRENT_BRANCH"
        if ! git rebase "$MAIN_BRANCH"; then
            print_error "Конфликт при rebase!"
            exit 1
        fi
    fi
    print_success "Текущая ветка перебазирована"
fi

# Возвращаемся на исходную ветку
if [ "$CURRENT_BRANCH" != "$MAIN_BRANCH" ]; then
    if [ "$DRY_RUN" = false ]; then
        git checkout "$CURRENT_BRANCH"
    fi
fi

print_success "✅ Умная синхронизация завершена!"

# Показываем статус
print_status "📊 Финальный статус:"
if [ "$DRY_RUN" = false ]; then
    echo "📁 Основной репозиторий:"
    git status --short
    
    echo ""
    echo "📋 Submodules:"
    git submodule status
    
    echo ""
    echo "🔄 Последние коммиты в main:"
    git log --oneline -3 "$MAIN_BRANCH"
fi

print_status "💡 Полезные команды для работы с submodules:"
echo "  Посмотреть локальные ветки в submodule:  cd ASFEnhance && git branch -a"
echo "  Восстановить изменения:                  cd ASFEnhance && git stash pop"
echo "  Переключиться на локальную ветку:        cd ASFEnhance && git checkout local-changes-*"
echo "  Принудительно обновить submodule:        git submodule update --force ASFEnhance" 