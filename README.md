# MonsterDex

深層迷宮（死者宮殿、天之御柱、正統優雷卡等）與優雷卡系列內容的怪物資料庫，供 DeepDungeonDex 插件讀取，讓玩家在遊戲內即時查詢怪物的威脅程度、仇恨感知類型、弱點屬性等資訊。

## 內容結構

- `DD/`：各深層迷宮依樓層分類的怪物資料（死者宮殿、天之御柱、正統優雷卡，以及尚未在台服開放的新深層迷宮）。
- `Eureka/`：優雷卡系列區域（如優雷卡豐水之地）的怪物資料。
- `Localization/`：多語言翻譯，透過 Crowdin 管理，含 `zh-TW` 繁體中文。
- `Territories.yml`：各深層迷宮對應的地圖 TerritoryType ID 範圍。
- `index.json`：資料檔案索引，供插件載入時使用。

此分支（`data-tc`）追蹤上游 [wolfcomp/MonsterDex](https://github.com/wolfcomp/MonsterDex) 的 `data` 分支，僅供台服版 DeepDungeonDex 讀取資料，不含插件原始碼。
