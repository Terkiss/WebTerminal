# Architecture Overview

## Purpose

??臾몄꽌???깆쓽 湲곕낯 ?꾨줈?앺듃 援ъ“? 怨꾩링 寃쎄퀎瑜??뺤쓽?쒕떎.

## Read When

- 援ъ“ 蹂寃? 湲곕뒫 援ы쁽, 由ы뙥?곕쭅 ?꾩뿉 layer boundary瑜??뺤씤????
- `core/`, `data/`, `domain/`, `view/`, `components/` 諛곗튂瑜??뺥븷 ??
- ?곹깭 愿由??섏〈??二쇱엯/?쇱슦??酉곕え???곹깭 ?듭????깆쓽 援ъ“瑜??먮떒????(?? Riverpod, Redux provider ?꾩튂 ?뚯븙 ??
- Clean Architecture, MVVM, Result/AppFailure 寃쎄퀎瑜??뺤씤????

## Update Policy

Antigravity/AGY???ъ슜???붿껌???덇굅?? ?ㅼ젣 肄붾뱶 援ъ“? ??臾몄꽌媛 異⑸룎?⑥쓣 蹂닿퀬?섍퀬 ?ъ슜???뱀씤??諛쏆? 寃쎌슦?먮쭔 ??臾몄꽌瑜??섏젙?쒕떎. ?깅퀎 援ъ“瑜?異붿륫?쇰줈 ?뺤젙?섏? ?딄퀬, ?뺤씤??肄붾뱶? ?ъ슜??寃곗젙留?諛섏쁺?쒕떎.

## Related Docs

`docs/architecture/data-model.md`, `docs/architecture/api.md`, `docs/architecture/auth-permissions.md`, `docs/development/conventions.md`, `docs/design/screen-flows.md`

## Canonical Project Tree

湲곕낯 ?뚯뒪 援ъ“???ㅼ쓬???곕Ⅸ?? ?꾨옒???덉떆 援ъ“?대ŉ, ?꾨줈?앺듃 湲곗닠 ?ㅽ깮???곕씪 寃쎈줈? ?뚯씪 ?뺤옣?먭? ?щ씪吏????덈떎 (?? Flutter??`lib/` 援ъ“, React??`src/` 援ъ“ ??.

```text
{source_root}/
  core/
    di/
      data/
      domain/
      view/
    error/
    router/
      app_router.{ext}
      route_guards.{ext}
      route_names.{ext}
      route_paths.{ext}
      routes/
    theme/
      dark_theme.{ext}
      light_theme.{ext}
      spacing/
        height_space.{ext}
        width_space.{ext}
  data/
    {feature}/
      dto/
      data_source/
      mapper/
      repository/
  domain/
    {feature}/
      entity/
      repository/
      use_case/
  view/
    {xx_screen}/
      {xx_screen}.{ext}
      {xx}_viewmodel.{ext}
      components/
        {local_component}/
          {local_component}.{ext}
          {local_component}_notifier.{ext}
  components/
```

`components/` (?먮뒗 `widgets/`)?????꾩뿭?먯꽌 ?ъ궗?⑸릺??怨듭슜 UI 而댄룷?뚰듃瑜??붾떎. ?뱀젙 ?붾㈃?먯꽌留??곕뒗 UI 而댄룷?뚰듃???대떦 `view/{xx_screen}/components/` ?꾨옒???붾떎.

## Layer Responsibilities

| Layer | Owns | Must Not Own |
| --- | --- | --- |
| `core/` | 怨듯넻 DI, error, router, theme, extension, base utility | feature-specific business logic |
| `data/` | DTO, external data source, mapper, repository implementation | UI state, UI 而댄룷?뚰듃, screen-specific logic |
| `domain/` | Entity, repository interface, use case, domain rule | UI, DTO serialization, concrete API/storage implementation |
| `view/` | screen, screen-local UI 而댄룷?뚰듃, viewmodel, screen-local notifier | DTO, data source, repository implementation |
| `components/` | ???꾩뿭 reusable UI 而댄룷?뚰듃 | screen-specific state or page-only UI 而댄룷?뚰듃 |

## Dependency Direction

Dependency???덉そ?쇰줈 ?ν븳??

```text
view -> domain
data -> domain
core -> shared support
```

- `viewmodel`? domain??use case ?먮뒗 repository interface瑜??ъ슜?쒕떎.
- `viewmodel`? DTO, data source, repository implementation??吏곸젒 ?ъ슜?섏? ?딅뒗??
- `data`??repository implementation? domain repository interface瑜?援ы쁽?쒕떎.
- `data_source`???몃? ?듭떊?대굹 local storage?먯꽌 raw/DTO ?곗씠?곕? 諛쏅뒗??
- repository implementation? raw/DTO瑜????대??먯꽌 ?곕뒗 `Entity`濡?蹂?섑빐 諛섑솚?쒕떎.

## Core Structure

`core/`?????꾩뿭 怨듯넻 ?붿냼瑜??붾떎.

| Folder | Role |
| --- | --- |
| `core/di/` | Provider wiring, dependency registration, shared provider setup |
| `core/error/` | App-level exception, failure, error mapping, error presentation contract |
| `core/router/` | App router, route names, route guards, navigation configuration |
| `core/theme/` | Theme, color, typography, spacing, common visual constants |

`core/di/`???곹깭 愿由?provider wiring (?? Riverpod, Redux provider wiring ?????덉씠?댁? ?대뜑 援ъ“??留욊쾶 遺꾨━?쒕떎.

| Folder | Role |
| --- | --- |
| `core/di/data/` | Data source, repository implementation provider wiring |
| `core/di/domain/` | Use case, domain repository interface provider wiring |
| `core/di/view/` | Screen viewmodel provider wiring |

- Page ?먮뒗 screen??`viewmodel` ?꾩슜 provider??`core/di/view/` ?꾨옒??screen 援ъ“??留욊쾶 ?붾떎.
- Repository, data source, use case provider??媛??덉씠?댁뿉 留욌뒗 `core/di/` ?섏쐞 ?대뜑???붾떎.
- ???섎굹??screen ?대? UI 而댄룷?뚰듃?먯꽌留??곕뒗 notifier provider???대떦 notifier? 媛숈? 而댄룷?뚰듃 folder???????덈떎.
- ?꾩뿭 怨듭슜 UI 而댄룷?뚰듃???щ윭 screen?먯꽌 ?곕뒗 provider??`core/di/` ?먮뒗 怨듭슜 ?꾩튂濡??щ┛??
- 湲곗〈 ?꾨줈?앺듃????援ъ껜?곸씤 DI 援ъ“媛 ?덉쑝硫?湲곗〈 援ъ“瑜??곗꽑?섎릺, layer boundary???좎??쒕떎.

`core/router/`???쇱슦???⑦꽩 (?? GoRouter, React Router ?? 湲곕컲 navigation??愿由ы븳??

| File | Role |
| --- | --- |
| `app_router.{ext}` | 理쒖쥌 router composition (?? ?쇱슦???몄뒪?댁뒪) |
| `route_paths.{ext}` | Route path constants |
| `route_names.{ext}` | Route name constants |
| `route_guards.{ext}` | Auth, onboarding, permission redirect rules |
| `routes/` | Feature or screen route groups |

Navigation rules:

- `viewmodel`? UI ?꾨젅?꾩썙?ъ쓽 context 媛앹껜 (?? BuildContext, React Context ??瑜??ъ슜?섏? ?딅뒗??
- Path 臾몄옄?댁쓣 screen?먯꽌 吏곸젒 ?곗? ?딅뒗??
- Route name/path??`core/router` ?곸닔留??ъ슜?쒕떎.
- Auth, onboarding 媛숈? ?꾩뿭 redirect??媛쒕퀎 screen???⑸퓣由ъ? ?딄퀬 guard?먯꽌 泥섎━?쒕떎.
- Path parameter??紐낇솗???대쫫???대떎. ?? `:userId`, `:orderId`.
- Query parameter??route builder?먯꽌 ?뚯떛?섍퀬, viewmodel?먮뒗 ?꾩슂??媛믩쭔 ?섍릿??

`core/theme/`????븷蹂??뚯씪濡??섎늿??

- `dark_theme.{ext}`: dark theme definition
- `light_theme.{ext}`: light theme definition
- `spacing/height_space.{ext}`: height spacing helper (?? Flutter??SizedBox, CSS margin 湲곕컲)
- `spacing/width_space.{ext}`: width spacing helper (?? Flutter??SizedBox, CSS margin 湲곕컲)

?꾨줈?앺듃???ㅻⅨ theme ?뚯씪紐낆씠 ?대? ?덉쑝硫?湲곗〈 naming???곗꽑?섎릺, dark/light/spacing 梨낆엫? 遺꾨━?쒕떎.

## Error And Result Boundary

?ㅽ뙣 泥섎━??data/domain 寃쎄퀎? view 寃쎄퀎瑜?遺꾨━?쒕떎.

```text
data_source
  -> may throw external exception
repository_impl
  -> catches exception
  -> maps to AppFailure
  -> returns Result<Entity>
use_case
  -> returns Result<T> when fallible
viewmodel
  -> consumes Result<T>
  -> converts to async state or screen-specific state (?? Riverpod AsyncValue<T>, React Suspense ?곹깭 ??
view
  -> handles async state or screen state
```

- `Result<T>`??repository interface? use case???ㅽ뙣 媛?ν븳 怨꾩빟???ъ슜?쒕떎.
- `Success<T>`???깃났 data瑜??대뒗??
- `Failure<T>`??`AppFailure`瑜??대뒗??
- `AppFailure`??`core/error/`???붾떎.
- `data_source`???몃? SDK/API exception??throw?????덈떎.
- `repository_impl`? ?몃? exception??catch?섍퀬 `AppFailure`濡?蹂?섑븳??
- `viewmodel`? `Result<T>`瑜??몃뒗 留덉?留?寃쎄퀎??
- `viewmodel`? ?깃났??data state濡? ?ㅽ뙣瑜??곹깭 愿由??먮윭 ?곹깭 (?? Riverpod AsyncError, Redux Error State ?? ?먮뒗 screen-specific error state濡?蹂?섑븳??
- `view`??`Result<T>`, DTO, data source, repository implementation??吏곸젒 ?ㅻ（吏 ?딅뒗??
- `repository`? `use_case`???곹깭 愿由?鍮꾨룞湲????(?? Riverpod AsyncValue ????諛섑솚?섏? ?딅뒗??

`Result<T>`??紐⑤뱺 ?⑥닔??媛뺤젣?섏? ?딅뒗?? Pure mapper, formatter, local calculation泥섎읆 ?ㅽ뙣媛 business contract媛 ?꾨땶 ?⑥닔???⑥닚 媛믪쓣 諛섑솚?쒕떎.

## Data Layer

`data/{feature}/`???몃? ?곗씠?곗? domain ?ъ씠??adapter ??븷???쒕떎.

```text
data/
  auth/
    dto/
      login_request_dto.{ext}
      login_response_dto.{ext}
      user_dto.{ext}
    data_source/
      auth_remote_data_source.{ext}
      auth_local_data_source.{ext}
    mapper/
      user_mapper.{ext}
    repository/
      auth_repository_impl.{ext}
```

- `dto/`: API, remote service, external input/output 援ъ“瑜??쒗쁽?쒕떎.
- `data_source/`: HTTP, Firebase, database, local storage 媛숈? ?ㅼ젣 ?낆텧?μ쓣 ?대떦?쒕떎.
- `mapper/`: DTO? Entity 蹂?섏쓣 ?대떦?쒕떎.
- `repository/`: domain repository interface??implementation???붾떎.

DTO??view濡??꾨떖?섏? ?딅뒗?? View?먯꽌 ?꾩슂??媛믪? repository implementation ?먮뒗 use case瑜?嫄곗퀜 `Entity`濡??꾨떖?쒕떎.

## Feature And Screen Boundary

- `data/{feature}`? `domain/{feature}`??business/domain capability 湲곗??쇰줈 ?섎늿??
- `view/{xx_screen}`? UI screen 湲곗??쇰줈 ?섎늿??
- Feature? screen? 1:1???꾩슂媛 ?녿떎.
- ?섎굹??screen? ?щ윭 domain feature瑜??ъ슜?????덇퀬, ?섎굹??feature???щ윭 screen?먯꽌 ?ъ슜?????덈떎.
- Screen ?대쫫??留욎텛湲??꾪빐 遺덊븘?뷀븳 data/domain feature瑜?留뚮뱾吏 ?딅뒗??

## Domain Layer

`domain/{feature}/`?????대? ?섎?? business rule???붾떎.

```text
domain/
  auth/
    entity/
      user_entity.{ext}
    repository/
      auth_repository.{ext}
    use_case/
      login_use_case.{ext}
      logout_use_case.{ext}
```

- `entity/`: ???대??먯꽌 ?덉젙?곸쑝濡??ъ슜?섎뒗 domain object瑜??붾떎.
- `repository/`: data layer媛 援ы쁽??interface瑜??붾떎.
- `use_case/`: ?섎? ?덈뒗 ???됰룞?대굹 business flow瑜??붾떎.

`use_case`???꾩닔媛 ?꾨땲?? ?⑥닚 repository pass-through?쇰㈃ `viewmodel`??domain repository interface瑜?吏곸젒 ?ъ슜?????덈떎. ?ㅼ쓬 以??섎굹媛 ?덉쑝硫?use case瑜??붾떎.

- ?щ윭 repository??data source 議고빀
- validation, permission, filtering, sorting, error mapping
- ?щ윭 ?붾㈃?먯꽌 ?ъ궗?⑸릺?????됰룞
- ?뚯뒪?명빐????domain rule
- login, logout, delete account泥섎읆 ?섎? ?덈뒗 ?ъ슜???됰룞

## View Layer

`view/`??screen ?⑥쐞濡?援ъ꽦?쒕떎.

```text
view/
  home_screen/
    home_screen.{ext}
    home_viewmodel.{ext}
    components/
      banner_carousel/
        banner_carousel.{ext}
        banner_carousel_notifier.{ext}
```

- `{xx_screen}.{ext}`: ?붾㈃ ?꾩껜 UI 而댄룷?뚰듃瑜??붾떎.
- `{xx}_viewmodel.{ext}`: ?붾㈃ ?꾩껜 ?곹깭, action, navigation trigger, child notifier orchestration???대떦?쒕떎.
- `components/`: ?대떦 screen?먯꽌留??곕뒗 ?섏쐞 UI 而댄룷?뚰듃瑜??붾떎.
- `{local_component}_notifier.{ext}`: ?뱀젙 ?섏쐞 UI 而댄룷?뚰듃媛 ?낅┰ ?곹깭瑜?媛吏????대떦 而댄룷?뚰듃 ?대뜑 ?덉뿉 ?붾떎.

`viewmodel` ?쒓린??`viewmodel`??洹몃?濡??ъ슜?쒕떎. ?? `home_viewmodel.{ext}`, `HomeViewmodel`.
`viewmodel`? UI ?꾨젅?꾩썙?ъ쓽 context 媛앹껜 (?? BuildContext, React Context ??瑜?蹂댁쑀?섍굅??context 湲곕컲 navigation??吏곸젒 ?ㅽ뻾?섏? ?딅뒗??

## State Management

?곹깭 愿由щ뒗 ?꾨줈?앺듃???곹깭 愿由??⑦꽩 (?? Riverpod, Redux toolkit, MobX ????湲곕낯?쇰줈 ?쒕떎.

- 肄붾뱶 ?앹꽦 湲곕컲 provider/notifier ?앹꽦???ъ슜?쒕떎 (?? @riverpod, Redux slice ??.
- ?앹꽦 ?뚯씪? 吏곸젒 ?섏젙?섏? ?딅뒗??
- ?붾㈃ ?꾩껜 orchestration? screen-level `viewmodel`???대떦?쒕떎.
- UI 而댄룷?뚰듃 ?대????ロ엺 local state???대떦 而댄룷?뚰듃 folder??`notifier`媛 ?대떦?????덈떎.
- Screen viewmodel provider??`core/di/view/`?먯꽌 wiring?쒕떎.
- ?⑥씪 screen-local UI 而댄룷?뚰듃 notifier provider??notifier? 媛숈? 而댄룷?뚰듃 folder???????덈떎.
- screen-level `viewmodel`? ?꾩슂??child notifier瑜?議고빀?섎릺, 紐⑤뱺 local state瑜?臾댁“嫄??뚯쑀?섏? ?딅뒗??

## Naming

?곸꽭 naming? `docs/development/conventions.md`瑜??곕Ⅸ??

- Domain object: `Entity`
- External input/output object: `Dto`
- Repository interface: `{feature}_repository.{ext}`
- Repository implementation: `{feature}_repository_impl.{ext}`
- Screen folder: `{xx}_screen/`
- Screen UI 而댄룷?뚰듃 file: `{xx}_screen.{ext}`
- Screen viewmodel file: `{xx}_viewmodel.{ext}`
- Local UI 而댄룷?뚰듃 notifier file: `{local_component}_notifier.{ext}`

## Architecture Rules

- 湲곗〈 ?꾨줈?앺듃 肄붾뱶媛 ?대? 議댁옱?섎㈃ 肄붾뱶 援ъ“瑜?癒쇱? ?뺤씤?쒕떎.
- ??臾몄꽌? 肄붾뱶媛 異⑸룎?섎㈃ 異⑸룎 ?댁슜??蹂닿퀬?섍퀬 媛깆떊 ?꾩슂?깆쓣 ?쒖븞?쒕떎.
- ??怨꾩링?대굹 abstraction? ?ㅼ젣 梨낆엫 李⑥씠? 蹂寃?寃⑸━瑜?留뚮뱾 ?뚮쭔 異붽??쒕떎.
- DTO? Entity???꾨뱶/梨낆엫??媛숇떎硫?layer purity留뚯쑝濡?以묐났 class瑜?留뚮뱾吏 ?딅뒗??
- Auth, privacy, data deletion, migration, secret, release config??high-risk濡??ㅻ，??
- ?щ윭 module???곹뼢??二쇨굅???섎룎由ш린 鍮꾩떬 寃곗젙? `docs/architecture/adr/`???④릿??
