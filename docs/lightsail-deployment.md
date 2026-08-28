# Lightsail Deployment

最安構成を優先した、AWS Lightsail 1 台構成のデプロイ手順です。初回セットアップは AWS CloudShell で上から順に実行する前提で書いています。

## 構成

- インフラ: Terraform で Lightsail Instance、Static IP、公開ポートを作成
- アプリ: Docker 化した API を public な GHCR イメージとして GitHub Actions から配備
- リバースプロキシ: Caddy
- データベース: SQLite を同居配置し、`/opt/horse-racing-prediction/app/data/eventstore.db` を永続化

この構成は単一ノード前提です。SQLite を使うため、複数台構成や自動スケールは想定していません。

## 関連ファイル

- `infra/lightsail`: Lightsail 用 Terraform
- `deploy`: Docker Compose と Caddy 設定
- `.github/workflows/infra-deploy.yml`: インフラ用 GitHub Actions
- `.github/workflows/app-deploy.yml`: アプリ用 GitHub Actions

## 0. CloudShell の準備

CloudShell では、AWS アカウント管理権限を持つセッションで作業します。

最初にコマンドの有無を確認します。

```bash
aws --version
gh --version
```

`gh` が未導入なら、CloudShell では公式 RPM repository を追加してインストールします。

```bash
sudo dnf config-manager --add-repo https://cli.github.com/packages/rpm/gh-cli.repo
sudo dnf install -y gh
```

GitHub CLI にログインします。

```bash
gh auth login -s repo
gh auth status
```

作業用の環境変数を設定します。

```bash
export AWS_REGION=us-east-1
export AWS_ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
export GITHUB_OWNER=chameleonhead
export GITHUB_REPO=HorseRacingPrediction
export GITHUB_BRANCH=main
export GITHUB_ENVIRONMENT=production
export TF_STATE_BUCKET=horse-racing-prediction-terraform-state
export TF_STATE_KEY=lightsail/production/terraform.tfstate
export TF_STATE_REGION=$AWS_REGION
export AWS_ROLE_NAME=GitHubActionsHorseRacingPredictionInfraRole
export AWS_POLICY_NAME=GitHubActionsHorseRacingPredictionInfraPolicy
export LIGHTSAIL_DOMAIN_NAME=api.example.com
export LIGHTSAIL_ACME_EMAIL=admin@example.com
export LIGHTSAIL_USERNAME=ubuntu
```

## 1. Terraform state 用 S3 バケットを作成する

```bash
aws s3api create-bucket \
  --bucket "$TF_STATE_BUCKET" \
  --region "$TF_STATE_REGION" \
```

```bash
aws s3api put-bucket-versioning \
  --bucket "$TF_STATE_BUCKET" \
  --versioning-configuration Status=Enabled

aws s3api put-bucket-encryption \
  --bucket "$TF_STATE_BUCKET" \
  --server-side-encryption-configuration '{
    "Rules": [
      {
        "ApplyServerSideEncryptionByDefault": {
          "SSEAlgorithm": "AES256"
        }
      }
    ]
  }'

aws s3api put-public-access-block \
  --bucket "$TF_STATE_BUCKET" \
  --public-access-block-configuration \
    BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true
```

## 2. GitHub OIDC Provider と IAM ロールを作成・更新する

以下は管理者権限を持つ CloudShell セッションで実行します。OIDC Provider は未作成の場合だけ作成します。

```bash
export GITHUB_OIDC_PROVIDER_ARN="arn:aws:iam::${AWS_ACCOUNT_ID}:oidc-provider/token.actions.githubusercontent.com"

if ! aws iam get-open-id-connect-provider \
  --open-id-connect-provider-arn "$GITHUB_OIDC_PROVIDER_ARN" >/dev/null 2>&1; then
  aws iam create-open-id-connect-provider \
    --url https://token.actions.githubusercontent.com \
    --client-id-list sts.amazonaws.com \
    --thumbprint-list 6938fd4d98bab03faadb97b34396831e3780aea1
fi
```

trust policy を作成し、ロールを新規作成または更新します。

```bash
cat > /tmp/github-oidc-trust-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Principal": {
        "Federated": "${GITHUB_OIDC_PROVIDER_ARN}"
      },
      "Action": "sts:AssumeRoleWithWebIdentity",
      "Condition": {
        "StringEquals": {
          "token.actions.githubusercontent.com:aud": "sts.amazonaws.com"
        },
        "StringLike": {
          "token.actions.githubusercontent.com:sub": [
            "repo:${GITHUB_OWNER}/${GITHUB_REPO}:ref:refs/heads/${GITHUB_BRANCH}",
            "repo:${GITHUB_OWNER}/${GITHUB_REPO}:pull_request",
            "repo:${GITHUB_OWNER}/${GITHUB_REPO}:environment:${GITHUB_ENVIRONMENT}"
          ]
        }
      }
    }
  ]
}
EOF

if aws iam get-role --role-name "$AWS_ROLE_NAME" >/dev/null 2>&1; then
  aws iam update-assume-role-policy \
    --role-name "$AWS_ROLE_NAME" \
    --policy-document file:///tmp/github-oidc-trust-policy.json
else
  aws iam create-role \
    --role-name "$AWS_ROLE_NAME" \
    --assume-role-policy-document file:///tmp/github-oidc-trust-policy.json
fi
```

デプロイポリシーを作成します。Terraform state バケット自体の初期設定、Lightsail、Collector の
ECR/SQS/Lambda、障害通知用 SNS/CloudWatch、および Collector が使用する IAM リソースを対象にします。

```bash
cat > /tmp/github-actions-infra-policy.json <<EOF
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "LightsailDeployment",
      "Effect": "Allow",
      "Action": "lightsail:*",
      "Resource": "*"
    },
    {
      "Sid": "TerraformStateBucketManagement",
      "Effect": "Allow",
      "Action": [
        "s3:CreateBucket", "s3:DeleteBucket", "s3:ListBucket", "s3:GetBucketLocation",
        "s3:GetBucketVersioning", "s3:PutBucketVersioning", "s3:GetEncryptionConfiguration",
        "s3:PutEncryptionConfiguration", "s3:GetBucketPublicAccessBlock", "s3:PutBucketPublicAccessBlock",
        "s3:ListBucketVersions"
      ],
      "Resource": "arn:aws:s3:::${TF_STATE_BUCKET}"
    },
    {
      "Sid": "TerraformStateObjectManagement",
      "Effect": "Allow",
      "Action": [
        "s3:GetObject", "s3:PutObject", "s3:DeleteObject", "s3:GetObjectVersion"
      ],
      "Resource": "arn:aws:s3:::${TF_STATE_BUCKET}/*"
    },
    {
      "Sid": "EcrAuthentication",
      "Effect": "Allow",
      "Action": "ecr:GetAuthorizationToken",
      "Resource": "*"
    },
    {
      "Sid": "CollectorEcrRepository",
      "Effect": "Allow",
      "Action": "ecr:*",
      "Resource": "arn:aws:ecr:${AWS_REGION}:${AWS_ACCOUNT_ID}:repository/horse-racing-prediction-collector"
    },
    {
      "Sid": "CollectorSqsQueues",
      "Effect": "Allow",
      "Action": "sqs:*",
      "Resource": [
        "arn:aws:sqs:${AWS_REGION}:${AWS_ACCOUNT_ID}:horse-racing-prediction-collector",
        "arn:aws:sqs:${AWS_REGION}:${AWS_ACCOUNT_ID}:horse-racing-prediction-collector-dlq"
      ]
    },
    {
      "Sid": "SqsDiscovery",
      "Effect": "Allow",
      "Action": "sqs:ListQueues",
      "Resource": "*"
    },
    {
      "Sid": "CollectorLambdaFunction",
      "Effect": "Allow",
      "Action": "lambda:*",
      "Resource": "arn:aws:lambda:${AWS_REGION}:${AWS_ACCOUNT_ID}:function:horse-racing-prediction-collector"
    },
    {
      "Sid": "LambdaEventSourceMappings",
      "Effect": "Allow",
      "Action": [
        "lambda:CreateEventSourceMapping", "lambda:DeleteEventSourceMapping", "lambda:GetEventSourceMapping",
        "lambda:ListEventSourceMappings", "lambda:ListTags", "lambda:TagResource", "lambda:UntagResource",
        "lambda:UpdateEventSourceMapping"
      ],
      "Resource": "*"
    },
    {
      "Sid": "CollectorLambdaExecutionRole",
      "Effect": "Allow",
      "Action": "iam:*",
      "Resource": "arn:aws:iam::${AWS_ACCOUNT_ID}:role/horse-racing-prediction-collector-lambda"
    },
    {
      "Sid": "LightsailApiIamUser",
      "Effect": "Allow",
      "Action": "iam:*",
      "Resource": "arn:aws:iam::${AWS_ACCOUNT_ID}:user/horse-racing-prediction-lightsail-api"
    },
    {
      "Sid": "ApiQueueSenderPolicy",
      "Effect": "Allow",
      "Action": "iam:*",
      "Resource": "arn:aws:iam::${AWS_ACCOUNT_ID}:policy/horse-racing-prediction-api-collection-queue-sender"
    },
    {
      "Sid": "CollectorAlertTopic",
      "Effect": "Allow",
      "Action": "sns:*",
      "Resource": "arn:aws:sns:${AWS_REGION}:${AWS_ACCOUNT_ID}:horse-racing-prediction-collector-alerts"
    },
    {
      "Sid": "SnsSubscriptionManagement",
      "Effect": "Allow",
      "Action": ["sns:GetSubscriptionAttributes", "sns:Unsubscribe"],
      "Resource": "*"
    },
    {
      "Sid": "CollectorCloudWatchAlarms",
      "Effect": "Allow",
      "Action": [
        "cloudwatch:DeleteAlarms", "cloudwatch:ListTagsForResource", "cloudwatch:PutMetricAlarm",
        "cloudwatch:TagResource", "cloudwatch:UntagResource"
      ],
      "Resource": "arn:aws:cloudwatch:${AWS_REGION}:${AWS_ACCOUNT_ID}:alarm:horse-racing-prediction-collector-*"
    },
    {
      "Sid": "CloudWatchAlarmDiscovery",
      "Effect": "Allow",
      "Action": "cloudwatch:DescribeAlarms",
      "Resource": "*"
    }
  ]
}
EOF
```

管理ポリシーが未作成なら作成し、作成済みなら新しい version を default にします。IAM 管理ポリシーは
最大5 version のため、更新前に古い非 default version を削除します。

```bash
export AWS_POLICY_ARN="arn:aws:iam::${AWS_ACCOUNT_ID}:policy/${AWS_POLICY_NAME}"

if aws iam get-policy --policy-arn "$AWS_POLICY_ARN" >/dev/null 2>&1; then
  aws iam list-policy-versions \
    --policy-arn "$AWS_POLICY_ARN" \
    --query 'Versions[?IsDefaultVersion==`false`].VersionId' \
    --output text | tr '\t' '\n' | while read -r version_id; do
      [ -n "$version_id" ] && aws iam delete-policy-version \
        --policy-arn "$AWS_POLICY_ARN" \
        --version-id "$version_id"
    done
  aws iam create-policy-version \
    --policy-arn "$AWS_POLICY_ARN" \
    --policy-document file:///tmp/github-actions-infra-policy.json \
    --set-as-default
else
  aws iam create-policy \
    --policy-name "$AWS_POLICY_NAME" \
    --policy-document file:///tmp/github-actions-infra-policy.json
fi

aws iam attach-role-policy \
  --role-name "$AWS_ROLE_NAME" \
  --policy-arn "$AWS_POLICY_ARN"
```

Event Source Mapping の状態参照を含む必須権限が反映されたことを確認します。すべて `allowed` になってから
`app-deploy` を再実行してください。

```bash
aws iam simulate-principal-policy \
  --policy-source-arn "arn:aws:iam::${AWS_ACCOUNT_ID}:role/${AWS_ROLE_NAME}" \
  --action-names \
    lambda:ListEventSourceMappings \
    lambda:GetEventSourceMapping \
    lambda:ListTags \
  --resource-arns \
    "arn:aws:lambda:${AWS_REGION}:${AWS_ACCOUNT_ID}:event-source-mapping:*" \
  --query 'EvaluationResults[].{Action:EvalActionName,Decision:EvalDecision}' \
  --output table

gh workflow run app-deploy.yml --ref "$GITHUB_BRANCH"
```

## 3. Lightsail 用 SSH 鍵を作成する

```bash
mkdir -p ~/.ssh
ssh-keygen -t ed25519 -C "horse-racing-prediction-lightsail" -f ~/.ssh/horse_racing_prediction_lightsail
```

必要なら確認します。

```bash
cat ~/.ssh/horse_racing_prediction_lightsail.pub
cat ~/.ssh/horse_racing_prediction_lightsail
```

## 4. GitHub Secrets を登録する

```bash
gh secret set AWS_ROLE_TO_ASSUME --body "arn:aws:iam::${AWS_ACCOUNT_ID}:role/${AWS_ROLE_NAME}"
gh secret set AWS_REGION --body "$AWS_REGION"
gh secret set AWS_AVAILABILITY_ZONE --body "${AWS_REGION}a"
gh secret set TF_STATE_BUCKET --body "$TF_STATE_BUCKET"
gh secret set TF_STATE_KEY --body "$TF_STATE_KEY"
gh secret set TF_STATE_REGION --body "$TF_STATE_REGION"
gh secret set LIGHTSAIL_SSH_PUBLIC_KEY < ~/.ssh/horse_racing_prediction_lightsail.pub
gh secret set LIGHTSAIL_SSH_PRIVATE_KEY < ~/.ssh/horse_racing_prediction_lightsail
gh secret set LIGHTSAIL_USERNAME --body "$LIGHTSAIL_USERNAME"
gh secret set LIGHTSAIL_DOMAIN_NAME --body "$LIGHTSAIL_DOMAIN_NAME"
gh secret set LIGHTSAIL_ACME_EMAIL --body "$LIGHTSAIL_ACME_EMAIL"
```

以下は値を決めてから登録します。

```bash
gh secret set LIGHTSAIL_API_KEY --body "$(openssl rand -base64 32)"
gh secret set ALERT_EMAIL --body "your-alert-address@example.com"
```

`ALERT_EMAIL` を設定すると、Collector ジョブ失敗、Lambda エラー、SQS DLQ 到達、キュー滞留を通知する
Amazon SNS のメール購読が作成されます。初回デプロイ後に AWS Notifications から届く確認メールの
`Confirm subscription` を実行するまで通知は配信されません。

GHCR は public イメージ前提なので、`GHCR_USERNAME` と `GHCR_PAT` は不要です。

確認:

```bash
gh secret list
```

## 5. GitHub Environment を作成する

```bash
gh api \
  --method PUT \
  -H "Accept: application/vnd.github+json" \
  "/repos/${GITHUB_OWNER}/${GITHUB_REPO}/environments/${GITHUB_ENVIRONMENT}"
```

## 6. infra-deploy workflow を実行する

```bash
gh workflow run infra-deploy.yml --ref "$GITHUB_BRANCH"
gh run list --workflow infra-deploy.yml --limit 5
gh run view --log
```

## 7. Static IP を控えて DNS を向ける

```bash
aws lightsail get-static-ips --query 'staticIps[].{Name:name,Ip:ipAddress,AttachedTo:attachedTo}' --output table
aws lightsail get-instances --query 'instances[].{Name:name,PublicIp:publicIpAddress,State:state.name}' --output table
```

表示された Static IP に公開ドメインの A レコードを向けます。

## 8. app workflow 用の残り Secrets を登録する

```bash
export LIGHTSAIL_HOST=$(aws lightsail get-static-ips --query 'staticIps[0].ipAddress' --output text)
gh secret set LIGHTSAIL_HOST --body "$LIGHTSAIL_HOST"
```

## 9. 初回 SSH 接続を確認する

```bash
ssh -i ~/.ssh/horse_racing_prediction_lightsail ${LIGHTSAIL_USERNAME}@${LIGHTSAIL_HOST}
```

接続後:

```bash
docker --version
docker compose version
ls -la /opt/horse-racing-prediction/app
ls -la /opt/horse-racing-prediction/app/data
exit
```

これらは [infra/lightsail/cloud-init.yaml.tftpl](../infra/lightsail/cloud-init.yaml.tftpl) の user data に依存しています。

## 10. app-deploy workflow を実行する

```bash
gh workflow run app-deploy.yml --ref "$GITHUB_BRANCH"
gh run list --workflow app-deploy.yml --limit 5
gh run view --log
```

初回は GitHub Packages の対象イメージを public に設定してください。package の visibility が private のままだと、Lightsail 側は認証なしで pull できません。

## 11. 動作確認をする

独自ドメインを登録していなくても、デプロイ時に `LIGHTSAIL_HOST`（静的IP）から `<IPをハイフン区切りにした文字列>.sslip.io` という形式のホスト名を自動的に導出し、Caddy がそのホスト名で Let's Encrypt の証明書を自動取得します。sslip.io はそのホスト名をそのまま埋め込まれたIPに解決するワイルドカードDNSサービスで、ドメインを購入・所有しなくても CA 発行の正規証明書を得られます。そのため `curl` や実クライアントは `-k` なしでそのまま接続できます。

```bash
export LIGHTSAIL_PUBLIC_HOSTNAME="$(echo "$LIGHTSAIL_HOST" | tr '.' '-').sslip.io"
curl -I https://${LIGHTSAIL_PUBLIC_HOSTNAME}/swagger/index.html
curl -I https://${LIGHTSAIL_PUBLIC_HOSTNAME}/swagger/v1/swagger.json
```

独自ドメインを別途用意している場合は、`LIGHTSAIL_DOMAIN_NAME`（と任意で `LIGHTSAIL_ACME_EMAIL`）を GitHub Secrets に設定すれば、そちらが優先されます（sslip.io は使われません）。

自己署名証明書は使用していません。IP に直接 HTTPS でアクセスすること（`https://${LIGHTSAIL_HOST}/...`）はサポートしておらず、`http://${LIGHTSAIL_HOST}/...` へのアクセスは上記のホスト名へ 301 リダイレクトされます。すべての通信経路が最終的に信頼された証明書での HTTPS に行き着く構成です。

API キー付きの確認例:

```bash
curl -H "X-Api-Key: <YOUR_API_KEY>" https://${LIGHTSAIL_PUBLIC_HOSTNAME}/api/races
```

## GitHub Secrets 一覧

### infra workflow 用

- `AWS_ROLE_TO_ASSUME`
- `AWS_REGION`
- `AWS_AVAILABILITY_ZONE`
- `TF_STATE_BUCKET`
- `TF_STATE_KEY`
- `TF_STATE_REGION`
- `LIGHTSAIL_SSH_PUBLIC_KEY`

### app workflow 用

- `LIGHTSAIL_HOST`
- `LIGHTSAIL_USERNAME`
- `LIGHTSAIL_SSH_PRIVATE_KEY`
- `LIGHTSAIL_DOMAIN_NAME` (独自ドメインを使う場合のみ。IP は設定しない)
- `LIGHTSAIL_ACME_EMAIL` (独自ドメインを使う場合のみ)
- `LIGHTSAIL_API_KEY`
- `ALERT_EMAIL` (Collector 障害通知先。未設定の場合は SNS トピックのみ作成)

## 運用メモ

- 独自ドメイン未設定時は、静的IPから導出した `sslip.io` ホスト名で Caddy が自動的に信頼された証明書を取得する（`.github/workflows/app-deploy.yml` 内で導出）
- API キーは `HORSE_RACING_API_KEY` としてコンテナへ注入される
- SQLite ファイルはサーバー上の `/opt/horse-racing-prediction/app/data/eventstore.db` に保存される

## SQLite Migration とバックアップ

デプロイ時はAPIコンテナを停止してから、次のバックアップを作成する。

```text
/opt/horse-racing-prediction/app/data/backups/eventstore-predeploy-YYYYMMDD-HHMMSS.db
```

Collector Lambda の Terraform に必要な ECR、Lambda、SQS、SNS、CloudWatch、IAM 権限も、上記の
`github-actions-infra-policy.json` で管理します。AWS リソースや workflow を変更した場合は、実際に必要な
操作と ARN を確認して同じポリシーを更新してください。AWS API の仕様上リソースレベルの制限ができない
discovery、認証、Lambda Event Source Mapping 操作などだけ `Resource = "*"` とします。

Api 用アクセスキーは Terraform の暗号化済み S3 backend に sensitive value として保存され、
アプリケーションデプロイ時に Lightsail の `/opt/horse-racing-prediction/app/.env` へ直接配置されます。
GitHub Secrets に AWS アクセスキーを登録する必要はありません。

その後、新しいAPIコンテナの起動時に以下を自動実行する。

1. `PRAGMA quick_check` による整合性確認
2. Migration直前のSQLiteオンラインバックアップ
3. 既存DBの初回ベースライン検証
4. EF Core Migration適用
5. 適用後の整合性確認

バックアップはデプロイ前コピーとMigration前コピーをそれぞれ直近7世代保持する。
Migrationまたは整合性確認に失敗した場合、APIはリクエスト受付前に終了する。

復旧時はコンテナを停止し、現在のDBを別名へ退避したうえで、選択したバックアップを
`eventstore.db` として復元し、直前のイメージタグで `docker compose up -d` を実行する。
稼働中のSQLiteファイルを直接上書きしてはならない。
- バックアップは Lightsail のスナップショットで取得する
- `docker compose` の更新のみでアプリを入れ替えるため、通常はインフラ workflow とアプリ workflow は互いのコードに依存しない
- ただし `infra-deploy` と `app-deploy` は同じ concurrency グループ（`horse-racing-prediction-production-deploy`）を共有しており、同時実行はせず直列化される。インフラ変更中に Lightsail のポート/静的IPが再構成され、アプリ側の SSH 接続がタイムアウトする事故を防ぐための措置
- `app-deploy` はリモートへの接続前に SSH (22番) の到達性を最大5分リトライし、デプロイ後は `/swagger/v1/swagger.json` へのHTTPSアクセスで起動確認を行う。どちらか失敗した場合はジョブが失敗として報告される
- OIDC の引き受けはローカル CLI で再現しづらいため、最終確認は `infra-deploy` workflow の plan / apply で行う
