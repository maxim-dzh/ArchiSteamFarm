# 1. Обновляем main из upstream
git checkout main
git fetch upstream
git merge upstream/main

# Обновить все submodules до последних коммитов
git submodule update --remote
git add .
git commit -m "submodule update"

git push origin main

# 2. Ребейзим свою feature ветку
git checkout feature
git rebase main

# Если есть конфликты:
# - исправляете их
# - git add .  
# - git rebase --continue

# 3. Пушим обновленную ветку (может потребоваться --force)
git push origin feature/my-plugin --force-with-lease