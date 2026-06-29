# Publish to GitHub — Desmatch Mode 4

**Статус:** `beta`  
**GitHub:** source backup only  
**Версия:** `3.0.20`  
**Deployment:** `(server_client,headless_all)`

## 1. Подготовка (уже сделано этим скриптом)

Папка: `github-repos/DesmatchMode4/`

## 2. Создать репозиторий и запушить

```powershell
cd github-repos/DesmatchMode4
git init
git add .
git commit -m "Source backup Desmatch Mode 4 v3.0.20"
git branch -M main
git remote add origin https://github.com/kabzon93region/DesmatchMode4.git
git push -u origin main
```

Или автоматически:

```powershell
python CURSORAIMODING/tools/publish/publish_github_release.py DesmatchMode4 --create-repo
```

## 3. GitHub Release

**Не создавать** — мод `beta` (только бэкап исходников).

Причина: Alpha/WIP — не отдавать вне нашего стенда.

Автоматически: `publish_github_release.py` пропустит Release для этого мода.

## Описание репозитория (suggested)

Fork DesmatchMode для SPT 4 + Fika 2.3. Сервер — скелет.

SPT 4.0 + Fika 2.3 headless stack. Deployment: `(server_client,headless_all)`.
