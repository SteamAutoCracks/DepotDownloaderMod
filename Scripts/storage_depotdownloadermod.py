import sys
import os
import re
import traceback
import time
import logging
import asyncio
import aiofiles
import colorlog
import httpx
import ujson as json
import vdf
import base64
import zlib
import struct
import pygob
import collections
import hashlib
import hmac
from typing import Any
from pathlib import Path
from colorama import init, Fore, Back, Style
from Crypto.Cipher import AES
from Crypto.Util.Padding import unpad
from cryptography.hazmat.primitives.ciphers.aead import ChaCha20Poly1305, AESGCM

init()
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

lock = asyncio.Lock()
client = httpx.AsyncClient(trust_env=True, verify=False)

DEPOTDOWNLOADER = "DepotDownloadermod.exe"
DEPOTDOWNLOADER_ARGS = "-max-downloads 256 -verify-all"

MANIFESTHUB_DOMAIN = "manifesthub2.filegear-sg.me"
MANIFESTHUB_WEB_URL = f"https://{MANIFESTHUB_DOMAIN}/"
MANIFESTHUB_API_URL = f"https://api.{MANIFESTHUB_DOMAIN}/manifest"

DEFAULT_CONFIG = {
    "Github_Personal_Token": "",
    "Custom_Steam_Path": "",
    "QA1": "温馨提示: Github_Personal_Token可在Github设置的最底下开发者选项找到, 详情看教程",
    "教程": "https://ikunshare.com/Onekey_tutorial"
}

LOG_FORMAT = '%(log_color)s%(message)s'
LOG_COLORS = {
    'INFO': 'cyan',
    'WARNING': 'yellow',
    'ERROR': 'red',
    'CRITICAL': 'purple',
}


def init_log(level=logging.DEBUG) -> logging.Logger:
    """ 初始化日志模块 """
    logger = logging.getLogger('Onekey')
    logger.setLevel(level)

    stream_handler = logging.StreamHandler()
    stream_handler.setLevel(level)

    fmt = colorlog.ColoredFormatter(LOG_FORMAT, log_colors=LOG_COLORS)
    stream_handler.setFormatter(fmt)

    if not logger.handlers:
        logger.addHandler(stream_handler)

    return logger


log = init_log()


def init():
    """ 输出初始化信息 """
    banner_lines = [
        "  _____   __   _   _____   _   _    _____  __    __ ",
        " /  _  \\ |  \\ | | | ____| | | / /  | ____| \\ \\  / /",
        " | | | | |   \\| | | |__   | |/ /   | |__    \\ \\/ /",
        " | | | | | |\\   | |  __|  | |\\ \\   |  __|    \\  / ",
        " | |_| | | | \\  | | |___  | | \\ \\  | |___    / /",
        " \\_____/ |_|  \\_| |_____| |_|  \\_\\ |_____|  /_/",
    ]
    for line in banner_lines:
        log.info(line)

    log.info('DepotDownloadermod下载脚本一键生成工具')
    log.info('原作者: ikun0014 修改: oureveryday')
    log.warning('本项目采用GNU General Public License v3开源许可证, 请勿用于商业用途')
    log.info(
        '项目Github仓库: https://github.com/oureveryday/DepotDownloaderMod'
    )
    log.warning(
        '本项目完全开源免费, 如果你在淘宝, QQ群内通过购买方式获得, 赶紧回去骂商家死全家\n   交流群组:\n    https://discord.gg/BZQtrBSUnd'
    )
    log.info('App ID可以在SteamDB, SteamUI或Steam商店链接页面查看')


def stack_error(exception: Exception) -> str:
    """ 处理错误堆栈 """
    stack_trace = traceback.format_exception(
        type(exception), exception, exception.__traceback__)
    return ''.join(stack_trace)


async def gen_config_file():
    """ 生成配置文件 """
    try:
        async with aiofiles.open("./config.json", mode="w", encoding="utf-8") as f:
            await f.write(json.dumps(DEFAULT_CONFIG, indent=2, ensure_ascii=False, escape_forward_slashes=False))

        log.info('程序可能为第一次启动或配置重置,请填写配置文件后重新启动程序')
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'配置文件生成失败,{stack_error(e)}')


async def load_config():
    """ 加载配置文件 """
    if not os.path.exists('./config.json'):
        await gen_config_file()
        os.system('pause')
        sys.exit()

    try:
        async with aiofiles.open("./config.json", mode="r", encoding="utf-8") as f:
            config = json.loads(await f.read())
            return config
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f"配置文件加载失败，原因: {stack_error(e)},重置配置文件中...")
        os.remove("./config.json")
        await gen_config_file()
        os.system('pause')
        sys.exit()

config = asyncio.run(load_config())


async def check_github_api_rate_limit(headers):
    """ 检查Github请求数 """

    if headers != None:
        log.info(f"您已配置Github Token")

    url = 'https://api.github.com/rate_limit'
    try:
        r = await client.get(url, headers=headers)
        r_json = r.json()
        if r.status_code == 200:
            rate_limit = r_json.get('rate', {})
            remaining_requests = rate_limit.get('remaining', 0)
            reset_time = rate_limit.get('reset', 0)
            reset_time_formatted = time.strftime(
                '%Y-%m-%d %H:%M:%S', time.localtime(reset_time))
            log.info(f'剩余请求次数: {remaining_requests}')
            if remaining_requests == 0:
                log.warning(f'GitHub API 请求数已用尽, 将在 {reset_time_formatted} 重置,建议生成一个填在配置文件里')
        else:
            log.error('Github请求数检查失败, 网络错误')
    except KeyboardInterrupt:
        log.info("程序已退出")
    except httpx.ConnectError as e:
        log.error(f'检查Github API 请求数失败, {stack_error(e)}')
    except httpx.ConnectTimeout as e:
        log.error(f'检查Github API 请求数超时: {stack_error(e)}')
    except Exception as e:
        log.error(f'发生错误: {stack_error(e)}')


async def checkcn() -> bool:
    try:
        req = await client.get('https://mips.kugou.com/check/iscn?&format=json')
        body = req.json()
        scn = bool(body['flag'])
        if (not scn):
            log.info(
                f"您在非中国大陆地区({body['country']})上使用了项目, 已自动切换回Github官方下载CDN")
            os.environ['IS_CN'] = 'no'
            return False
        else:
            os.environ['IS_CN'] = 'yes'
            return True
    except KeyboardInterrupt:
        log.info("程序已退出")
    except httpx.ConnectError as e:
        os.environ['IS_CN'] = 'yes'
        log.warning('检查服务器位置失败，已忽略，自动认为你在中国大陆')
        log.warning(stack_error(e))
        return False
    
def csharp_gzip(b64_string):
    # Base64 解码
    compressed_data = base64.b64decode(b64_string)
    

    if len(compressed_data) <= 18:
        raise ValueError("Data too short to be gzip format")
        
    decompressor = zlib.decompressobj(-zlib.MAX_WBITS)
    # skip gz header
    decompressed_data = decompressor.decompress(compressed_data[10:])
    decompressed_data += decompressor.flush()
    
    return decompressed_data.decode('utf-8')

# 逆向提取的真实密钥
KS_CRYPTO_KEY_SEED = (
    "5c75bffb8ebf9cfbc3c83f7f557c4cdd3056406606d0355dac61a4001dde68a8"
)
KEY = hashlib.sha256(bytes.fromhex(KS_CRYPTO_KEY_SEED)).digest()
CHACHA = ChaCha20Poly1305(KEY)


def decrypt_ks_string(val: str) -> str:
    if not val.startswith("shiki~"):
        return val
    enc = val[6:]
    pad = (4 - len(enc) % 4) % 4
    try:
        raw = base64.urlsafe_b64decode(enc + "=" * pad)
        nonce, ct = raw[:12], raw[12:]
        return CHACHA.decrypt(nonce, ct, None).decode("utf-8")
    except Exception:
        return val


def decrypt_ks_text(text: str) -> str:
    pattern = re.compile(
        r'((?:addappid|addtoken)\s*\(\s*\d+\s*(?:,\s*\d+\s*)?,\s*")([^"]*)(")'
    )
    return pattern.sub(
        lambda m: m.group(1) + decrypt_ks_string(m.group(2)) + m.group(3), text
    )


# ShikiLuaQwQ 仓库与加密常量
QWQ_RAW_GITHUB_ROOT = "https://raw.githubusercontent.com"
QWQ_REPO_OWNER = "ShikieikiC"
QWQ_REPO_NAME = "ShikiLuaQwQ"
QWQ_BRANCH = "main"
QWQ_ROOT_DIR = "QwQ"

QWQ_BUCKET_KEY = "SHIKIEIKISUKI"
QWQ_BUCKET_PREFIX = "SHIKI"
QWQ_BUCKET_SUFFIX = "KAWAII"
QWQ_BUCKET_COUNT = 400
QWQ_BUCKET_HASH_LEN = 16

QWQ_FILENAME_CRYPTO_KEY = "6325179eccb19fe387dcb64b85058d30a1b388dbe684eb1d79cb43e74bd6e931"
QWQ_CONTENT_CRYPTO_KEY = "9f8a4fb9bd3322ee1eb83bb316a868918327fdd81b219a10a923b57dcf58f03e"

QWQ_MAGIC = b"KSL3"
QWQ_VERSION = b"\x01"
QWQ_NONCE_SIZE = 12


def get_qwq_bucket_name(app_id: int | str) -> str:
    """根据 AppID 计算分桶目录"""
    norm_id = str(app_id).strip()
    h1 = hashlib.sha256((QWQ_BUCKET_KEY + norm_id).encode("utf-8")).digest()
    bucket_idx = int.from_bytes(h1[:8], "big") % QWQ_BUCKET_COUNT
    h2 = hashlib.sha256((QWQ_BUCKET_KEY + f"bucket:{bucket_idx}").encode("utf-8")).hexdigest()
    return f"{QWQ_BUCKET_PREFIX}{h2[:QWQ_BUCKET_HASH_LEN]}{QWQ_BUCKET_SUFFIX}"


def get_qwq_encrypted_file_name(app_id: int | str, bucket_name: str = None) -> str:
    """根据 AppID 计算加密文件名"""
    norm_id = str(app_id).strip()
    if bucket_name is None:
        bucket_name = get_qwq_bucket_name(norm_id)

    name_key = hmac.new(
        bytes.fromhex(QWQ_FILENAME_CRYPTO_KEY),
        b"KSL3-NAME\x00" + bucket_name.strip().encode("utf-8"),
        hashlib.sha256
    ).digest()

    nonce = hmac.new(name_key, norm_id.encode("utf-8"), hashlib.sha256).digest()[:12]
    aesgcm = AESGCM(name_key)
    ciphertext = aesgcm.encrypt(nonce, norm_id.encode("utf-8"), None)

    payload = nonce + ciphertext
    encoded = base64.urlsafe_b64encode(payload).decode("utf-8").rstrip("=")
    return f"{encoded}.qwq"


def decrypt_qwq_payload(payload: bytes, bucket_name: str, encrypted_name: str) -> str:
    """
    解密 .qwq 文件二进制数据
    """
    header = QWQ_MAGIC + QWQ_VERSION
    if not payload.startswith(header):
        raise ValueError(f"无效文件头！期望 {header}，实际为 {payload[:5]}")

    offset = len(header)
    nonce = payload[offset : offset + QWQ_NONCE_SIZE]
    ciphertext = payload[offset + QWQ_NONCE_SIZE :]

    # 1. 路径上下文
    path_context = f"{bucket_name}/{encrypted_name}".encode("utf-8")

    # 2. 派生 ChaCha20 解密 Key
    content_key = hmac.new(
        bytes.fromhex(QWQ_CONTENT_CRYPTO_KEY),
        b"KSL3-CONTENT\x00" + path_context,
        hashlib.sha256
    ).digest()

    # 3. 关联认证数据 (AAD): b"KSL3\x01\x00" + path_context
    associated_data = QWQ_MAGIC + QWQ_VERSION + b"\x00" + path_context

    # 4. ChaCha20-Poly1305 解密
    cipher = ChaCha20Poly1305(content_key)
    decrypted_bytes = cipher.decrypt(nonce, ciphertext, associated_data)

    # 5. 去除 BOM 和首尾空白
    text = decrypted_bytes.decode("utf-8", errors="ignore")
    return text.lstrip("\ufeff").strip()


def decrypt_qwq_file(file_path: str, bucket_name: str = None, app_id: str = None) -> str:
    """
    解密本地已保存的 .qwq 文件
    """
    filename = os.path.basename(file_path)
    if bucket_name is None and app_id is not None:
        bucket_name = get_qwq_bucket_name(app_id)
    with open(file_path, "rb") as f:
        payload = f.read()
    return decrypt_qwq_payload(payload, bucket_name, filename)


async def get(sha: str, path: str, repo: str):
    if os.environ.get('IS_CN') == 'yes':
        url_list = [
            f'https://jsdelivr.pai233.top/gh/{repo}@{sha}/{path}',
            f'https://cdn.jsdmirror.com/gh/{repo}@{sha}/{path}',
            f'https://raw.gitmirror.com/{repo}/{sha}/{path}',
            f'https://raw.dgithub.xyz/{repo}/{sha}/{path}',
            f'https://gh.akass.cn/{repo}/{sha}/{path}'
        ]
    else:
        url_list = [
            f'https://raw.githubusercontent.com/{repo}/{sha}/{path}'
        ]
    retry = 3
    while retry > 0:
        for url in url_list:
            try:
                r = await client.get(url, timeout=30)
                if r.status_code == 200:
                    return r.read()
                else:
                    log.error(f'获取失败: {path} - 状态码: {r.status_code}')
            except KeyboardInterrupt:
                log.info("程序已退出")
            except httpx.ConnectError as e:
                log.error(f'获取失败: {path} - 连接错误: {str(e)}')
            except httpx.ConnectTimeout as e:
                log.error(f'连接超时: {url} - 错误: {str(e)}')

        retry -= 1
        log.warning(f'重试剩余次数: {retry} - {path}')

    log.error(f'超过最大重试次数: {path}')
    raise Exception(f'无法下载: {path}')

async def get_manifest(app_id: str, sha: str, path: str, repo: str) -> list:
    collected_depots = []
    depot_cache_path = Path(os.getcwd())
    try:
        if path.endswith('.manifest'):
            save_path = depot_cache_path / path
            if save_path.exists():
                log.warning(f'已存在清单: {save_path}')
                return collected_depots
            content = await get(sha, path, repo)
            log.info(f'清单下载成功: {path}')
            async with aiofiles.open(save_path, 'wb') as f:
                await f.write(content)
        elif path == 'Key.vdf' or path == 'key.vdf':
            content = await get(sha, path, repo)
            log.info(f'密钥下载成功: {path}')
            depots_config = vdf.loads(content.decode('utf-8'))
            if depots_config:
                async with aiofiles.open(depot_cache_path / f"{app_id}.key", 'w', encoding="utf-8") as f:
                    for depot_id, depot_info in depots_config['depots'].items():
                        if (repo == 'sean-who/ManifestAutoUpdate'):
                            decryptedkey = await xor_decrypt(b"Scalping dogs, I'll fuck you",bytearray.fromhex(depot_info["DecryptionKey"]))
                            await f.write(f'{depot_id};{decryptedkey.decode("utf-8")}\n')
                        else:
                            await f.write(f'{depot_id};{depot_info["DecryptionKey"]}\n')
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'处理失败: {path} - {stack_error(e)}')
        raise
    return collected_depots

async def get_data(app_id: str, path: str, repo: str) -> list:
    AppInfo = collections.namedtuple('AppInfo', ['Appid','Licenses', 'App', 'Depots', 'EncryptedAppTicket', 'AppOwnershipTicket', 'unknown', 'GameConnectAuthTicket'])
    collected_depots = []
    depot_cache_path = Path(os.getcwd())
    try:
        content = await get('main', path, repo)
        content_dec = await symmetric_decrypt(b" s  t  e  a  m  ", content)
        content_dec = await xor_decrypt(b"hail",content_dec)
        content_gob = pygob.load_all(bytes(content_dec))
        app_info = AppInfo._make(*content_gob)
        keyfile = await aiofiles.open(depot_cache_path / f"{app_id}.key", 'w', encoding="utf-8")
        for depot in app_info.Depots:
            filename = f"{depot.Id}_{depot.Manifests.Id}.manifest"
            save_path = depot_cache_path / filename
            if save_path.exists():
                log.warning(f'已存在清单: {save_path}')
            else:
                async with aiofiles.open(save_path, 'wb') as f:
                    await f.write(depot.Manifests.Data)
            await keyfile.write(f'{depot.Id};{depot.Decryptkey.hex()}\n')
            collected_depots.append(filename)
        keyfile.close()
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'处理失败: {path} - {stack_error(e)}')
        raise
    return collected_depots

async def get_data_local(app_id: str) -> list:
    collected_depots = []
    depot_cache_path = Path(os.getcwd())
    try:
        lua_file_path = depot_cache_path / f"{app_id}.lua"
        st_file_path = depot_cache_path / f"{app_id}.st"
        ks_file_path = depot_cache_path / f"{app_id}.ks"
        qwq_file_path = depot_cache_path / f"{app_id}.qwq"
        if not lua_file_path.exists() and not st_file_path.exists() and not ks_file_path.exists() and not qwq_file_path.exists():
            log.error(f'未找到lua文件: {lua_file_path}、st文件: {st_file_path}、ks文件: {ks_file_path} 或 qwq文件: {qwq_file_path}')
            raise FileNotFoundError

        contents = []
        if lua_file_path.exists():
            async with aiofiles.open(lua_file_path, 'r', encoding="utf-8", errors="ignore") as f:
                contents.append(await f.read())

        if ks_file_path.exists():
            async with aiofiles.open(ks_file_path, 'r', encoding="utf-8", errors="ignore") as f:
                contents.append(await f.read())

        if qwq_file_path.exists():
            async with aiofiles.open(qwq_file_path, 'rb') as f:
                qwq_bytes = await f.read()
            bucket = get_qwq_bucket_name(app_id)
            enc_name = get_qwq_encrypted_file_name(app_id, bucket)
            try:
                contents.append(decrypt_qwq_payload(qwq_bytes, bucket, enc_name))
            except Exception:
                contents.append(decrypt_qwq_payload(qwq_bytes, bucket, qwq_file_path.name))

        if st_file_path.exists():
            async with aiofiles.open(st_file_path, 'rb') as f:
                st_bytes = await f.read()
            # 解析 header
            header = st_bytes[:12]
            xorkey, size, xorkeyverify = struct.unpack('III', header)
            xorkey ^= 0xFFFEA4C8
            xorkey &= 0xFF
            # 解析 data
            data = bytearray(st_bytes[12:12+size])
            for i in range(len(data)):
                data[i] = data[i] ^ xorkey
            # 读取 data
            decompressed_data = zlib.decompress(data)
            contents.append(decompressed_data[512:].decode('utf-8', errors="ignore"))

        content = "\n".join(contents)
        content = decrypt_ks_text(content)

        async with aiofiles.open(depot_cache_path / f"{app_id}.key", 'w', encoding="utf-8") as keyfile:
            # 解析 addappid/addtoken 和 setManifestid
            addappid_pattern = re.compile(r'(?:addappid|addtoken)\s*\(\s*(\d+)\s*(?:,\s*\d+\s*,\s*["\']([0-9a-zA-Z]+)["\']\s*)?\)', re.IGNORECASE)
            setmanifestid_pattern = re.compile(r'set[Mm]anifest[iI]d\s*\(\s*(\d+)\s*,\s*["\']?(\d+)["\']?(?:\s*,\s*["\']?(\d+)["\']?)?\s*\)')

            for match in addappid_pattern.finditer(content):
                depot_id = match.group(1)
                decrypt_key = match.group(2) if match.group(2) else None
                if decrypt_key:
                    log.info(f'解析到 addappid/addtoken: depot_id={depot_id}, decrypt_key={decrypt_key}')
                    await keyfile.write(f'{depot_id};{decrypt_key}\n')

        seen_manifests = set()
        for match in setmanifestid_pattern.finditer(content):
            depot_id = match.group(1)
            manifest_id = match.group(2)
            filename = f"{depot_id}_{manifest_id}.manifest"
            save_path = depot_cache_path / filename
            log.info(f'解析到 setManifestid: depot_id={depot_id}, manifest_id={manifest_id}')
            if filename not in seen_manifests:
                seen_manifests.add(filename)
                collected_depots.append(filename)
            if save_path.exists():
                log.info(f'存在清单: {save_path}')
            else:
                log.info(f'未找到清单: {save_path}')
            
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'处理失败: {stack_error(e)}')
        raise
    return collected_depots

async def check_and_download_missing_manifests(manifests: list):
    """检测 manifest 文件是否存在，如有缺失则提示用户并从 API 下载"""
    depot_cache_path = Path(os.getcwd())
    missing_manifests = []
    
    for manifest in manifests:
        m_name = Path(manifest).name
        m_path = depot_cache_path / m_name
        if not m_path.exists():
            name = m_path.stem
            parts = name.split('_')
            if len(parts) >= 2 and parts[0].isdigit() and parts[1].isdigit():
                missing_manifests.append((parts[0], parts[1], m_name, m_path))

    if not missing_manifests:
        log.info("所有 manifest 清单文件均已存在在本文件夹。")
        return

    log.warning(f"\n检测到 {len(missing_manifests)} 个 manifest 清单文件在本地不存在：")
    for depot_id, manifest_id, m_name, _ in missing_manifests:
        log.warning(f"  缺失: {m_name} (DepotID: {depot_id}, ManifestID: {manifest_id})")

    user_choice = input(f"\n{Fore.YELLOW}是否下载缺失的 manifest 清单文件？(y/n): {Style.RESET_ALL}").strip().lower()
    if user_choice not in ['y', 'yes', '是', '1']:
        log.info("已跳过下载缺失的 manifest 文件。")
        return

    print(f"\n{Fore.CYAN}获取 API Key 地址: {Fore.GREEN}{MANIFESTHUB_WEB_URL}{Style.RESET_ALL} {Fore.CYAN}[Steam Manifest API Key Generator]{Style.RESET_ALL}")
    print(f"{Fore.YELLOW}提示: API Key 不会保存到任何文件，每次均需手动输入。{Style.RESET_ALL}")
    apikey = input(f"{Fore.CYAN}请输入 API Key: {Style.RESET_ALL}").strip()

    if not apikey:
        log.error("未输入 API Key，取消下载。")
        return

    for depot_id, manifest_id, m_name, m_path in missing_manifests:
        url = f"{MANIFESTHUB_API_URL}?apikey={apikey}&depotid={depot_id}&manifestid={manifest_id}"
        log.info(f"正在从 API 下载清单: {m_name} ...")
        try:
            r = await client.get(url, timeout=60)
            if r.status_code == 200 and r.content:
                if r.content.startswith(b'{') and b'"error"' in r.content:
                    try:
                        err_json = r.json()
                        err_msg = err_json.get('error') or err_json.get('message') or r.text
                        log.error(f"下载清单失败: {m_name} - API 返回错误: {err_msg}")
                        continue
                    except Exception:
                        pass
                async with aiofiles.open(m_path, 'wb') as f:
                    await f.write(r.content)
                log.info(f"清单下载成功: {m_name}")
            else:
                log.error(f"下载清单失败: {m_name} - HTTP 状态码: {r.status_code}")
        except KeyboardInterrupt:
            log.info("程序已退出")
            return
        except Exception as e:
            log.error(f"下载清单异常: {m_name} - {stack_error(e)}")

async def depotdownloadermod_add(app_id: str, manifests: list) -> bool:
    async with lock:
        log.info(f'DepotDownloader 下载文件生成: {app_id}.bat')
        try:
            async with aiofiles.open(f'{app_id}.bat', mode="w", encoding="utf-8") as bat_file:
                for manifest in manifests:
                    depot_id = manifest[0:manifest.find('_')]
                    manifest_id = manifest[manifest.find('_') + 1:manifest.find('.')]
                    await bat_file.write(f'{DEPOTDOWNLOADER} -app {app_id} -depot {depot_id} -manifest {manifest_id} -manifestfile {manifest} -depotkeys {app_id}.key {DEPOTDOWNLOADER_ARGS}\n')
            await check_and_download_missing_manifests(manifests)
            return True
        except Exception as e:
            log.error(f'出现错误: {e}')
            return False

async def fetch_info(url, headers) -> str | None:
    try:
        r = await client.get(url, headers=headers)
        return r.json()
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'获取信息失败: {stack_error(e)}')
        return None
    except httpx.ConnectTimeout as e:
        log.error(f'获取信息时超时: {stack_error(e)}')
        return None
    
async def get_pro_token():
    try:
        r = await client.get("https://gitee.com/pjy612/sai/raw/master/free")
        return csharp_gzip(r.text)
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'获取信息失败: {stack_error(e)}')
        return None
    except httpx.ConnectTimeout as e:
        log.error(f'获取信息时超时: {stack_error(e)}')
        return None
    
async def symmetric_decrypt(key, ciphertext):
    """
    使用AES解密数据
    key: AES密钥字节串
    ciphertext: 待解密的字节串,包含IV
    """
    try:
    # 分离IV和加密数据
        iv = ciphertext[:AES.block_size]
        data = ciphertext[AES.block_size:]
        
        # 使用ECB模式解密IV
        cipher_ecb = AES.new(key, AES.MODE_ECB)
        iv = cipher_ecb.decrypt(iv)
        
        # 使用CBC模式和解密后的IV解密数据
        cipher_cbc = AES.new(key, AES.MODE_CBC, iv)
        decrypted = cipher_cbc.decrypt(data)
        
        # 去除PKCS7填充
        return unpad(decrypted, AES.block_size)
    except Exception as e:
        log.error(f'解密失败: {stack_error(e)}')
        return None

async def xor_decrypt(key, ciphertext):
    """
    使用异或解密数据
    key: 异或密钥字节串
    ciphertext: 待解密的字节串
    """
    try:
        decrypted = bytearray(len(ciphertext))
        for i in range(len(ciphertext)):
            decrypted[i] = ciphertext[i] ^ key[i % len(key)]
        return bytes(decrypted)
    except Exception as e:
        log.error(f'解密失败: {stack_error(e)}')
        return None

async def get_latest_repo_info(repos: list, app_id: str, headers) -> Any | None:
    if len(repos) == 1:
        return repos[0], None
        
    latest_date = None
    selected_repo = None
    for repo in repos:
        if repo == "luckygametools/steam-cfg" or repo.startswith("Steam tools"):
            continue
            
        url = f'https://api.github.com/repos/{repo}/branches/{app_id}'
        r_json = await fetch_info(url, headers)
        if r_json and 'commit' in r_json:
            date = r_json['commit']['commit']['author']['date']
            if (latest_date is None) or (date > latest_date):
                latest_date = date
                selected_repo = repo

    return selected_repo, latest_date

async def printedwaste_download(app_id: str) -> bool:
    url = f"https://api.printedwaste.com/gfk/download/{app_id}"
    headers = {
        "Authorization": "Bearer dGhpc19pcyBhX3JhbmRvbV90b2tlbg=="
    }
    depot_cache_path = Path(os.getcwd())
    try:
        r = await client.get(url, headers=headers, timeout=60)
        r.raise_for_status()
        content = await r.aread()  # 异步读取全部内容
        
        import io, zipfile
        zip_mem = io.BytesIO(content)
        with zipfile.ZipFile(zip_mem) as zf:
            for file in zf.namelist():
                if file.endswith(('.st', '.lua', '.ks', '.manifest')):
                    file_content = zf.read(file)
                    log.info(f"解压文件: {file}，大小: {len(file_content)} 字节")    
                    async with aiofiles.open(depot_cache_path / Path(file).name, 'wb') as f:
                        await f.write(file_content)        
        return True
    except httpx.HTTPStatusError as e:
        if e.response.status_code == 404:
            log.error("未找到清单")
            return False
        else:
            log.error(f'处理失败: {stack_error(e)}')
            raise
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'处理失败: {stack_error(e)}')
        raise

async def gdata_download(app_id: str) -> bool:
    url = f"https://steambox.gdata.fun/cnhz/qingdan/{app_id}.zip"
    depot_cache_path = Path(os.getcwd())
    try:
        r = await client.get(url, timeout=60)
        r.raise_for_status()
        content = await r.aread()  # 异步读取全部内容
        
        import io, zipfile
        zip_mem = io.BytesIO(content)
        with zipfile.ZipFile(zip_mem) as zf:
            for file in zf.namelist():
                if file.endswith(('.st', '.lua', '.ks', '.manifest')):
                    file_content = zf.read(file)
                    log.info(f"解压文件: {file}，大小: {len(file_content)} 字节")    
                    async with aiofiles.open(depot_cache_path / Path(file).name, 'wb') as f:
                        await f.write(file_content)        
        return True
    except httpx.HTTPStatusError as e:
        if e.response.status_code == 404:
            log.error("未找到清单")
            return False
        else:
            log.error(f'处理失败: {stack_error(e)}')
            raise
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'处理失败: {stack_error(e)}')
        raise

async def cysaw_download(app_id: str) -> bool:
    url = f"https://cysaw.top/uploads/{app_id}.zip"
    depot_cache_path = Path(os.getcwd())
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537.3"
    }
    try:
        r = await client.get(url, headers=headers, timeout=60)
        r.raise_for_status()
        content = await r.aread()  # 异步读取全部内容
        
        import io, zipfile
        zip_mem = io.BytesIO(content)
        with zipfile.ZipFile(zip_mem) as zf:
            for file in zf.namelist():
                if file.endswith(('.st', '.lua', '.ks', '.manifest')):
                    file_content = zf.read(file)
                    log.info(f"解压文件: {file}，大小: {len(file_content)} 字节")    
                    async with aiofiles.open(depot_cache_path / Path(file).name, 'wb') as f:
                        await f.write(file_content)        
        return True
    except httpx.HTTPStatusError as e:
        if e.response.status_code == 404:
            log.error("未找到清单")
            return False
        if e.response.status_code == 403:
            log.error("清单403")
            return False
        else:
            log.error(f'处理失败: {stack_error(e)}')
            raise
    except KeyboardInterrupt:
        log.info("程序已退出")
    except Exception as e:
        log.error(f'处理失败: {stack_error(e)}')
        raise

async def shikilua_qwq_download(app_id: str) -> bool:
    bucket = get_qwq_bucket_name(app_id)
    filename = get_qwq_encrypted_file_name(app_id, bucket)
    relative_path = f"{QWQ_ROOT_DIR}/{bucket}/{filename}"
    repo = f"{QWQ_REPO_OWNER}/{QWQ_REPO_NAME}"
    depot_cache_path = Path(os.getcwd())

    if os.environ.get('IS_CN') == 'yes':
        url_list = [
            f'https://ghfast.top/https://raw.githubusercontent.com/{repo}/{QWQ_BRANCH}/{relative_path}',
            f'https://jsdelivr.pai233.top/gh/{repo}@{QWQ_BRANCH}/{relative_path}',
            f'https://cdn.jsdmirror.com/gh/{repo}@{QWQ_BRANCH}/{relative_path}',
            f'https://raw.gitmirror.com/{repo}/{QWQ_BRANCH}/{relative_path}',
            f'https://raw.dgithub.xyz/{repo}/{QWQ_BRANCH}/{relative_path}',
            f'https://gh.akass.cn/{repo}/{QWQ_BRANCH}/{relative_path}',
            f'https://raw.githubusercontent.com/{repo}/{QWQ_BRANCH}/{relative_path}'
        ]
    else:
        url_list = [
            f'https://raw.githubusercontent.com/{repo}/{QWQ_BRANCH}/{relative_path}'
        ]

    retry = 3
    content = None
    while retry > 0:
        for url in url_list:
            try:
                r = await client.get(url, timeout=30)
                if r.status_code == 200:
                    content = r.read()
                    break
                elif r.status_code == 404:
                    log.error(f'获取失败: {relative_path} - 状态码: 404')
                    return False
                else:
                    log.error(f'获取失败: {relative_path} - 状态码: {r.status_code}')
            except KeyboardInterrupt:
                log.info("程序已退出")
                return False
            except httpx.ConnectError as e:
                log.error(f'获取失败: {relative_path} - 连接错误: {str(e)}')
            except httpx.ConnectTimeout as e:
                log.error(f'连接超时: {url} - 错误: {str(e)}')
        if content is not None:
            break
        retry -= 1
        log.warning(f'重试剩余次数: {retry} - {relative_path}')

    if not content:
        log.error(f'超过最大重试次数: {relative_path}')
        return False

    try:
        decrypted_lua = decrypt_qwq_payload(content, bucket, filename)
        log.info(f"解密成功: {app_id}.lua")
        async with aiofiles.open(depot_cache_path / f"{app_id}.lua", 'w', encoding="utf-8") as f:
            await f.write(decrypted_lua)
        return True
    except Exception as e:
        log.error(f'解密失败: {relative_path} - {stack_error(e)}')
        return False

async def main(app_id: str, repos: list) -> bool:
    app_id_list = list(filter(str.isdecimal, app_id.strip().split('-')))
    if not app_id_list:
        log.error(f'App ID无效')
        return False
    app_id = app_id_list[0]
    github_token = config.get("Github_Personal_Token", "")
    headers = {'Authorization': f'Bearer {github_token}'} if github_token else None
    # selected_repo, latest_date = await get_latest_repo_info(repos, app_id, headers)
    for selected_repo in repos:
        try:
            if (selected_repo):
                log.info(f'选择清单仓库: {selected_repo}')
            if selected_repo.startswith('Steam tools'):
                manifests = await get_data_local(app_id)
                await depotdownloadermod_add(app_id, manifests)
                log.info('已添加下载文件')
                # log.info(f'清单最后更新时间: {latest_date}')
                log.info(f'导入成功: {app_id}')
                await client.aclose()
                os.system('pause')
                return True
            elif selected_repo == 'PrintedWaste':
                if(await printedwaste_download(app_id)):
                    manifests = await get_data_local(app_id)
                    await depotdownloadermod_add(app_id, manifests)
                    log.info('已添加下载文件')
                    # log.info(f'清单最后更新时间: {latest_date}')
                    log.info(f'导入成功: {app_id}')
                    await client.aclose()
                    os.system('pause')
                    return True
            elif selected_repo == 'steambox.gdata.fun':
                if(await gdata_download(app_id)):
                    manifests = await get_data_local(app_id)
                    await depotdownloadermod_add(app_id, manifests)
                    log.info('已添加下载文件')
                    # log.info(f'清单最后更新时间: {latest_date}')
                    log.info(f'导入成功: {app_id}')
                    await client.aclose()
                    os.system('pause')
                    return True
            elif selected_repo == 'cysaw.top':
                if(await cysaw_download(app_id)):
                    manifests = await get_data_local(app_id)
                    await depotdownloadermod_add(app_id, manifests)
                    log.info('已添加下载文件')
                    # log.info(f'清单最后更新时间: {latest_date}')
                    log.info(f'导入成功: {app_id}')
                    await client.aclose()
                    os.system('pause')
                    return True
            elif selected_repo == 'ShikieikiC/ShikiLuaQwQ':
                await checkcn()
                if(await shikilua_qwq_download(app_id)):
                    manifests = await get_data_local(app_id)
                    await depotdownloadermod_add(app_id, manifests)
                    log.info('已添加下载文件')
                    # log.info(f'清单最后更新时间: {latest_date}')
                    log.info(f'导入成功: {app_id}')
                    await client.aclose()
                    os.system('pause')
                    return True
            elif selected_repo == 'luckygametools/steam-cfg': 
                await checkcn()
                await check_github_api_rate_limit(headers)
                url = f'https://api.github.com/repos/{selected_repo}/contents/steamdb2/{app_id}'
                r_json = await fetch_info(url, headers)
                if (r_json) and (isinstance(r_json, list)):
                    path = [item['path'] for item in r_json if item['name'] == '00000encrypt.dat'][0]
                    manifests = await get_data(app_id, path, selected_repo)
                    await depotdownloadermod_add(app_id, manifests)
                    log.info('已添加下载文件')
                    # log.info(f'清单最后更新时间: {latest_date}')
                    log.info(f'导入成功: {app_id}')
                    await client.aclose()
                    os.system('pause')
                    return True
            else:
                await checkcn()
                await check_github_api_rate_limit(headers)
                url = f'https://api.github.com/repos/{selected_repo}/branches/{app_id}'
                r_json = await fetch_info(url, headers)
                if (r_json) and ('commit' in r_json):
                    sha = r_json['commit']['sha']
                    url = r_json['commit']['commit']['tree']['url']
                    r2_json = await fetch_info(url, headers)
                    if (r2_json) and ('tree' in r2_json):
                        manifests = [item['path'] for item in r2_json['tree'] if item['path'].endswith('.manifest')]
                        for item in r2_json['tree']:
                            await get_manifest(app_id, sha, item['path'], selected_repo)
                        await depotdownloadermod_add(app_id, manifests)
                        log.info('已添加下载文件')
                        # log.info(f'清单最后更新时间: {latest_date}')
                        log.info(f'导入成功: {app_id}')
                        await client.aclose()
                        os.system('pause')
                        return True
        except Exception as e:
            log.error(f'处理失败: {stack_error(e)}')
        log.error(f'未找到清单: {app_id}')
    log.error(f'清单下载或生成失败: {app_id}')
    await client.aclose()
    os.system('pause')
    return False

def select_repo(repos):
    print(f"\n{Fore.YELLOW}{Back.BLACK}{Style.BRIGHT}请选择要使用的存储库：{Style.RESET_ALL}")
    print(f"{Fore.GREEN}1. 全部仓库{Style.RESET_ALL}")
    for i, repo in enumerate(repos, 2):
        print(f"{Fore.GREEN}{i}. {repo}{Style.RESET_ALL}")
    
    while True:
        try:
            choice = int(input(f"\n{Fore.CYAN}请输入数字选择: {Style.RESET_ALL}"))
            if 1 <= choice <= len(repos) + 1:
                if choice == 1:
                    return repos
                else:
                    return [repos[choice-2]]
            else:
                print(f"{Fore.RED}无效的选择，请重试{Style.RESET_ALL}")
        except ValueError:
            print(f"{Fore.RED}请输入有效的数字{Style.RESET_ALL}")

if __name__ == '__main__':
    init()
    try:
        repos = [
            'ikun0014/ManifestHub',
            'Auiowu/ManifestAutoUpdate',
            'tymolu233/ManifestAutoUpdate',
            'SteamAutoCracks/ManifestHub',
            'ShikieikiC/ShikiLuaQwQ',
            'PrintedWaste',
            'steambox.gdata.fun',
            'cysaw.top',
#            'P-ToyStore/SteamManifestCache_Pro'
            'sean-who/ManifestAutoUpdate',
            'luckygametools/steam-cfg',
            'Steam tools .lua/.st/.ks/.qwq script (Local file)'
        ]
        app_id = input(f"{Fore.CYAN}{Back.BLACK}{Style.BRIGHT}请输入游戏AppID: {Style.RESET_ALL}").strip()
        selected_repos = select_repo(repos)
        asyncio.run(main(app_id, selected_repos))
    except KeyboardInterrupt:
        log.info("程序已退出")
    except SystemExit:
        sys.exit()
